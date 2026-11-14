/*
 * Read-only store snapshot. No account ID, cookie, token or request header
 * is returned to the native host.
 */
(async()=>{
  try {
    const common = await import(__COMMON_PATH__);
    const store = common[__STORE_KEY__];
    if (!store || typeof store.isLoggedIn !== 'boolean')
      throw new Error('count-store-missing');

    const account = String(store.userInfo?.cuid ?? '');
    const encoded = new TextEncoder().encode('native-refill:' + account);
    const hash = Array.from(new Uint8Array(
      await crypto.subtle.digest('SHA-256', encoded)))
      .map(value => value.toString(16).padStart(2, '0'))
      .join('');

    return {
      ok: true,
      loggedIn: store.isLoggedIn,
      accountKey: hash,
      available: Number(store.totalClickCount),
      cumulative: Number(store.cumulativeClickCount),
      local: Number(store.localClickCount),
      consumed: Number(store.serverClickConsume),
      serverTotal: Number(store.serverClickTotal)
    };
  } catch (error) {
    return {
      ok: false,
      error: String(error && error.message ? error.message : error)
    };
  }
})()
