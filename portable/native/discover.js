/*
 * Page capability discovery for the local Wuwa Tappo WebView.
 * The contract-checking approach is derived from kano0222/WuwaTappo-helper
 * (MIT License); this implementation returns only module paths and export
 * names needed by the isolated Native Refill Host.
 */
(async()=>{
  try {
    if (location.hostname !== 'tauri.localhost')
      throw new Error('not-tauri-main-window');

    const deadline = Date.now() + 10000;
    const cache = new Map();
    let sources = [];
    for (;;) {
      const urls = [...new Set(
        [...document.scripts].map(x => x.src)
          .concat(performance.getEntriesByType('resource').map(x => x.name))
      )]
        .map(x => new URL(x, location.href))
        .filter(x => x.origin === location.origin &&
          /^\/assets\/[\w.-]+\.js$/.test(x.pathname))
        .map(x => x.pathname);

      if (urls.length > 160)
        throw new Error('asset-list-too-large');

      await Promise.all(urls.filter(x => !cache.has(x)).map(async path => {
        const response = await fetch(path, {
          signal: AbortSignal.timeout(Math.max(1, Math.min(5000, deadline - Date.now())))
        });
        if (!response.ok)
          throw new Error('asset-fetch-failed');
        const text = await response.text();
        if (text.length > 4000000)
          throw new Error('asset-too-large');
        cache.set(path, text);
      }));

      sources = [...cache];
      const main = sources.filter(x =>
        x[1].includes('case"boost-local-clicks":') ||
        x[1].includes('case"pet-clap":'));
      const store = sources.filter(x =>
        x[1].includes('function userInfo()') &&
        (x[1].includes('incrementLocalClickCount') ||
         x[1].includes('addLocalClickCount')));
      const clicks = sources.filter(x =>
        x[1].includes('"save_key_press_count"') &&
        x[1].includes('localClickCount'));

      if (main.length === 1 && store.length === 1 && clicks.length === 1)
        break;
      if (Date.now() >= deadline)
        throw new Error('main-assets-not-ready');
      await new Promise(resolve => setTimeout(resolve, 250));
    }

    const mainSource = sources.filter(x =>
      x[1].includes('case"boost-local-clicks":') ||
      x[1].includes('case"pet-clap":'))[0][1];
    const commonPath = sources.filter(x =>
      x[1].includes('function userInfo()') &&
      (x[1].includes('incrementLocalClickCount') ||
       x[1].includes('addLocalClickCount')))[0][0];
    const clickPath = sources.filter(x =>
      x[1].includes('"save_key_press_count"') &&
      x[1].includes('localClickCount'))[0][0];

    const common = await import(commonPath);
    const storeKeys = Object.keys(common).filter(key => {
      const value = common[key];
      return value && typeof value === 'object' &&
        typeof value.isLoggedIn === 'boolean' &&
        typeof value.totalClickCount === 'number' &&
        typeof value.cumulativeClickCount === 'number' &&
        typeof value.localClickCount === 'number' &&
        typeof value.serverClickConsume === 'number' &&
        (typeof value.incrementLocalClickCount === 'function' ||
         typeof value.addLocalClickCount === 'function');
    });
    if (storeKeys.length !== 1)
      throw new Error('count-store-not-unique');
    const storeKey = storeKeys[0];
    const store = common[storeKey];

    const clickModule = await import(clickPath);
    const saveKeys = Object.keys(clickModule).filter(key => {
      const value = clickModule[key];
      if (typeof value !== 'function')
        return false;
      const text = Function.prototype.toString.call(value);
      return text.includes('save_key_press_count') &&
        text.includes('localClickCount');
    });
    if (saveKeys.length !== 1)
      throw new Error('click-save-function-not-unique');

    let countMode;
    if (mainSource.includes('case"boost-local-clicks":'))
      countMode = 'event';
    else if (mainSource.includes('case"pet-clap":') &&
             typeof store.incrementLocalClickCount === 'function')
      countMode = 'increment';
    else
      throw new Error('count-handler-not-supported');

    return {
      ok: true,
      commonPath,
      clickPath,
      storeKey,
      saveKey: saveKeys[0],
      countMode
    };
  } catch (error) {
    return {
      ok: false,
      error: String(error && error.message ? error.message : error)
    };
  }
})()
