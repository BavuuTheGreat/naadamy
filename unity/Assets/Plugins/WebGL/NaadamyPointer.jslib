// What only the browser knows. The first-person view: Esc in a browser frees a locked mouse without the game ever seeing
// the key, and Unity keeps asking for the lock again on the next click, so FirstPerson.cs asks here who has the mouse.
// The layout: screen pixels per CSS pixel, so the game's units match the web version's sizes (GameUI.cs), and how far a
// wheel or trackpad really scrolled.
mergeInto(LibraryManager.library, {
  /** 1: the game has the mouse; 0: the page has it back while it still has focus (Esc); -1: the page lost focus. */
  NaadamyPointerLock: function () {
    if (document.pointerLockElement) return 1;
    return document.hasFocus() ? 0 : -1;
  },
  /** How far the wheel or trackpad scrolled since the last call, in CSS pixels (Unity only says which way, one step per event). */
  NaadamyWheel: function () {
    var w = window.naadamyWheel;
    if (!w) {
      w = window.naadamyWheel = { acc: 0 };
      window.addEventListener('wheel', function (e) { w.acc += e.deltaY * (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? window.innerHeight : 1); }, { passive: true, capture: true });
    }
    var v = w.acc; w.acc = 0; return v;
  },
  /** The page's devicePixelRatio, capped at 2 like the canvas (WebGLTemplates/Naadamy/index.html). */
  NaadamyDpr: function () { return Math.min(window.devicePixelRatio || 1, 2); },
  /** Opens `url` in a new tab when the pointer that is down now comes up. A browser only lets a page open a tab inside a
   *  click, and Unity hears the click a frame later, so GoogleSignIn.cs calls this on the button's pointer-down and the
   *  tab opens on the same press's pointer-up. Returns at once; a press that never comes up opens nothing after 5 s. */
  NaadamyOpenOnRelease: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var events = ['pointerup', 'touchend', 'mouseup'];
    var done = false;
    var stop = function () { events.forEach(function (e) { window.removeEventListener(e, open, true); }); };
    var open = function () { if (done) return; done = true; stop(); window.open(url, '_blank', 'noopener'); };
    events.forEach(function (e) { window.addEventListener(e, open, true); });
    setTimeout(function () { done = true; stop(); }, 5000);
  },
});
