const nodeFilter = window.NodeFilter ?? {
	SHOW_ALL: 0xFFFFFFFF,
	SHOW_ELEMENT: 0x1,
	SHOW_TEXT: 0x4,
	FILTER_ACCEPT: 1,
	FILTER_REJECT: 2,
	FILTER_SKIP: 3,
};

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
Object.defineProperty(window, 'matchMedia', {
  configurable: true,
  value: (query: string) => ({ matches: false, media: query, onchange: null,
    addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; } }),
});

// jsdom logs a "not implemented" error before returning null. Tests that need
// a real context install their own focused mock.
Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', {
  configurable: true,
  writable: true,
  value: () => null,
});

Object.defineProperty(globalThis, 'NodeFilter', {
	value: nodeFilter,
	configurable: true,
	writable: true,
});

Object.defineProperty(window, 'NodeFilter', {
	value: nodeFilter,
	configurable: true,
	writable: true,
});

Object.defineProperty(global, 'NodeFilter', {
	value: nodeFilter,
	configurable: true,
	writable: true,
});
