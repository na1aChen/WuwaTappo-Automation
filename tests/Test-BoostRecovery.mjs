import { createHash } from "node:crypto";
import {
  mkdtemp,
  readFile,
  rm,
  writeFile
} from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import assert from "node:assert/strict";

const projectRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  ".."
);
const boostSource = await readFile(
  path.join(projectRoot, "portable", "native", "boost.js"),
  "utf8"
);
const temporaryDirectory = await mkdtemp(
  path.join(tmpdir(), "wuwa-boost-recovery-")
);
const clickPath = path.join(temporaryDirectory, "click.mjs");
const accountKey = createHash("sha256")
  .update("native-refill:test-user")
  .digest("hex");
const realSetTimeout = globalThis.setTimeout;
let passed = 0;

await writeFile(
  clickPath,
  "export const save = (...args) => globalThis.__wuwaSave(...args);\n",
  "utf8"
);

try {
  globalThis.setTimeout = (callback, _delay, ...args) =>
    realSetTimeout(callback, 0, ...args);

  const localReset = await runBoost({
    expected: counts(100000, 200000, 7000, 896000),
    store: makeStore(counts(100000, 200000, 5000, 900000), (index) => {
      globalThis.__wuwaStore.cumulativeClickCount += 1;
      globalThis.__wuwaStore.totalClickCount += 1;
      if (index === 2)
        globalThis.__wuwaStore.localClickCount = 0;
      else
        globalThis.__wuwaStore.localClickCount += 1;
    }),
    delta: 5
  });
  assert.equal(localReset.result.ok, true);
  assert.equal(localReset.result.after.available, 100005);
  assert.equal(localReset.saveCalls, 1);
  pass("预检忽略 local/consumed 变化并吸收批次内 local 归零");

  const preflightChanged = await runBoost({
    expected: counts(100000, 200000, 5000, 896000),
    store: makeStore(counts(100001, 200001, 5000, 896000)),
    delta: 5
  });
  assert.equal(preflightChanged.result.error, "store-state-changed");
  assert.equal(preflightChanged.result.actual.available, 100001);
  assert.equal(preflightChanged.saveCalls, 0);
  pass("预检核心计数漂移返回可重基准现场");

  const partial = await runBoost({
    expected: counts(100000, 200000, 5000, 896000),
    store: makeStore(counts(100000, 200000, 5000, 896000), (index) => {
      globalThis.__wuwaStore.cumulativeClickCount += 1;
      globalThis.__wuwaStore.totalClickCount += 1;
      if (index === 3)
        globalThis.__wuwaStore.totalClickCount -= 1;
    }),
    delta: 10
  });
  assert.equal(partial.result.error, "increment-not-confirmed");
  assert.equal(partial.result.actual.available, 100003);
  assert.equal(partial.saveCalls, 1);
  pass("增量中断先保存可证明部分并返回实际快照");

  const saveFailed = await runBoost({
    expected: counts(100000, 200000, 5000, 896000),
    store: makeStore(counts(100000, 200000, 5000, 896000)),
    delta: 3,
    save: async () => {
      throw new Error("mock-save-failed");
    }
  });
  assert.match(saveFailed.result.error, /^save-failed:/);
  assert.equal(saveFailed.saveCalls, 3);
  pass("保存失败只重试保存三次");

  const postSave = await runBoost({
    expected: counts(100000, 200000, 5000, 896000),
    store: makeStore(counts(100000, 200000, 5000, 896000)),
    delta: 3,
    save: async () => {
      globalThis.__wuwaStore.totalClickCount = 100000;
      globalThis.__wuwaStore.cumulativeClickCount = 200000;
    }
  });
  assert.equal(postSave.result.error, "post-save-state-not-confirmed");
  assert.equal(postSave.saveCalls, 1);
  pass("保存后回滚不重发批次");

  console.log(`SUMMARY passed=${passed} failed=0`);
}
catch (error) {
  console.error(error);
  console.log(`SUMMARY passed=${passed} failed=1`);
  process.exitCode = 1;
}
finally {
  globalThis.setTimeout = realSetTimeout;
  delete globalThis.__wuwaStore;
  delete globalThis.__wuwaSave;
  await rm(temporaryDirectory, { recursive: true, force: true });
}

function counts(available, cumulative, local, consumed) {
  return { available, cumulative, local, consumed };
}

function makeStore(before, increment) {
  let index = 0;
  return {
    isLoggedIn: true,
    userInfo: { cuid: "test-user" },
    totalClickCount: before.available,
    cumulativeClickCount: before.cumulative,
    localClickCount: before.local,
    serverClickConsume: before.consumed,
    serverClickTotal: 0,
    incrementLocalClickCount() {
      if (increment)
        increment(index);
      else {
        this.totalClickCount += 1;
        this.cumulativeClickCount += 1;
        this.localClickCount += 1;
      }
      index += 1;
    }
  };
}

async function runBoost({
  expected,
  store,
  delta,
  save = async () => {}
}) {
  globalThis.__wuwaStore = store;
  let saveCalls = 0;
  globalThis.__wuwaSave = async (...args) => {
    saveCalls += 1;
    return save(...args);
  };

  const scriptPath = path.join(
    temporaryDirectory,
    `boost-${Math.random().toString(16).slice(2)}.mjs`
  );
  const commonPath = path.join(
    temporaryDirectory,
    `common-${Math.random().toString(16).slice(2)}.mjs`
  );
  await writeFile(
    commonPath,
    "export const store = globalThis.__wuwaStore;\n",
    "utf8"
  );
  const transformed = boostSource
    .replace("(async()=>{", "export default (async()=>{")
    .replaceAll("__COMMON_PATH__", JSON.stringify(pathToFileURL(commonPath).href))
    .replaceAll("__STORE_KEY__", JSON.stringify("store"))
    .replaceAll("__CLICK_PATH__", JSON.stringify(pathToFileURL(clickPath).href))
    .replaceAll("__SAVE_KEY__", JSON.stringify("save"))
    .replaceAll("__COUNT_MODE__", JSON.stringify("increment"))
    .replaceAll("__ACCOUNT_KEY__", JSON.stringify(accountKey))
    .replaceAll("__DELTA__", String(delta))
    .replaceAll("__BEFORE_AVAILABLE__", String(expected.available))
    .replaceAll("__BEFORE_CUMULATIVE__", String(expected.cumulative))
    .replaceAll("__BEFORE_LOCAL__", String(expected.local))
    .replaceAll("__BEFORE_CONSUMED__", String(expected.consumed));
  await writeFile(scriptPath, transformed, "utf8");
  const module = await import(`${pathToFileURL(scriptPath).href}?v=${Date.now()}`);
  const result = await module.default;
  return { result, saveCalls };
}

function pass(name) {
  passed += 1;
  console.log(`PASS ${name}`);
}
