/*
 * Increment the current page's existing local counter, save through the
 * application's own save_key_press_count function, and verify the receipt.
 * The design is derived from kano0222/WuwaTappo-helper (MIT License).
 */
(async()=>{
  try {
    const common = await import(__COMMON_PATH__);
    const store = common[__STORE_KEY__];
    const clickModule = await import(__CLICK_PATH__);
    const saveClicks = clickModule[__SAVE_KEY__];
    const delta = Number(__DELTA__);
    if (!store || typeof saveClicks !== 'function')
      throw new Error('native-capability-missing');
    if (!Number.isSafeInteger(delta) || delta < 1 || delta > 2000)
      throw new Error('delta-out-of-range');
    if (__COUNT_MODE__ !== 'increment')
      throw new Error('event-count-mode-not-supported');

    const account = String(store.userInfo?.cuid ?? '');
    const encoded = new TextEncoder().encode('native-refill:' + account);
    const accountKey = Array.from(new Uint8Array(
      await crypto.subtle.digest('SHA-256', encoded)))
      .map(value => value.toString(16).padStart(2, '0'))
      .join('');
    const readCounts = () => ({
      available: Number(store.totalClickCount),
      cumulative: Number(store.cumulativeClickCount),
      local: Number(store.localClickCount),
      consumed: Number(store.serverClickConsume),
      serverTotal: Number(store.serverClickTotal),
      loggedIn: store.isLoggedIn,
      accountKey
    });
    if (!store.isLoggedIn || accountKey !== __ACCOUNT_KEY__)
      return {
        ok:false,
        error:'login-or-account-changed',
        actual:readCounts()
      };

    const before = readCounts();
    if (before.available !== __BEFORE_AVAILABLE__ ||
        before.cumulative !== __BEFORE_CUMULATIVE__)
      return {
        ok:false,
        error:'store-state-changed',
        expected:{
          available:__BEFORE_AVAILABLE__,
          cumulative:__BEFORE_CUMULATIVE__,
          local:__BEFORE_LOCAL__,
          consumed:__BEFORE_CONSUMED__
        },
        before,
        actual:before
      };

    const saveWithRetry = async () => {
      let saveError = '';
      for (let attempt = 0; attempt < 3; attempt++) {
        try {
          await saveClicks(undefined, {force:true});
          return '';
        } catch (error) {
          saveError = String(error && error.message ? error.message : error);
          await new Promise(resolve => setTimeout(resolve, 500 * (attempt + 1)));
        }
      }
      return saveError;
    };

    for (let index = 0; index < delta; index++) {
      store.incrementLocalClickCount();
      if (Number(store.cumulativeClickCount) !== before.cumulative + index + 1 ||
          Number(store.totalClickCount) !== before.available + index + 1) {
        const actual = readCounts();
        if (!actual.loggedIn || actual.accountKey !== accountKey)
          return {
            ok:false,
            error:'login-or-account-changed',
            actual
          };
        const partialIsSafe =
          actual.available >= before.available &&
          actual.cumulative >= before.cumulative;
        const saveError = partialIsSafe ? await saveWithRetry() : '';
        if (saveError)
          return {
            ok:false,
            error:'save-failed:' + saveError,
            delta,
            before,
            actual:readCounts(),
            failed_index:index
          };
        return {
          ok:false,
          error:'increment-not-confirmed',
          delta,
          before,
          actual,
          failed_index:index
        };
      }
    }

    const saveError = await saveWithRetry();
    if (saveError)
      return {
        ok:false,
        error:'save-failed:' + saveError,
        delta,
        before,
        actual:readCounts()
      };

    await new Promise(resolve => setTimeout(resolve, 500));
    const after = readCounts();
    const confirmed = after.loggedIn &&
      String(store.userInfo?.cuid ?? '') === account &&
      after.cumulative >= before.cumulative + delta &&
      after.available >= before.available + delta;
    return {
      ok: confirmed,
      error: confirmed ? '' : 'post-save-state-not-confirmed',
      delta,
      expected:{
        available:before.available + delta,
        cumulative:before.cumulative + delta
      },
      before,
      after
    };
  } catch (error) {
    return {
      ok: false,
      error: String(error && error.message ? error.message : error)
    };
  }
})()
