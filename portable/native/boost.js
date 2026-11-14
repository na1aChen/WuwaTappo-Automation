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
    if (!store.isLoggedIn || accountKey !== __ACCOUNT_KEY__)
      return {ok:false,error:'login-or-account-changed'};

    const before = {
      available: Number(store.totalClickCount),
      cumulative: Number(store.cumulativeClickCount),
      local: Number(store.localClickCount),
      consumed: Number(store.serverClickConsume)
    };
    if (before.available !== __BEFORE_AVAILABLE__ ||
        before.cumulative !== __BEFORE_CUMULATIVE__ ||
        before.local !== __BEFORE_LOCAL__ ||
        before.consumed !== __BEFORE_CONSUMED__)
      return {ok:false,error:'store-state-changed',before};

    for (let index = 0; index < delta; index++) {
      store.incrementLocalClickCount();
      if (Number(store.localClickCount) !== before.local + index + 1 ||
          Number(store.cumulativeClickCount) !== before.cumulative + index + 1 ||
          Number(store.totalClickCount) !== before.available + index + 1)
        throw new Error('increment-not-confirmed');
    }

    let saveError = '';
    for (let attempt = 0; attempt < 3; attempt++) {
      try {
        await saveClicks(undefined, {force:true});
        saveError = '';
        break;
      } catch (error) {
        saveError = String(error && error.message ? error.message : error);
        await new Promise(resolve => setTimeout(resolve, 500 * (attempt + 1)));
      }
    }
    if (saveError)
      return {ok:false,error:'save-failed:' + saveError,before};

    await new Promise(resolve => setTimeout(resolve, 500));
    const after = {
      available: Number(store.totalClickCount),
      cumulative: Number(store.cumulativeClickCount),
      local: Number(store.localClickCount),
      consumed: Number(store.serverClickConsume),
      serverTotal: Number(store.serverClickTotal),
      loggedIn: store.isLoggedIn
    };
    const confirmed = after.loggedIn &&
      String(store.userInfo?.cuid ?? '') === account &&
      after.cumulative >= before.cumulative + delta &&
      after.available >= before.available + delta;
    return {
      ok: confirmed,
      error: confirmed ? '' : 'post-save-state-not-confirmed',
      delta,
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
