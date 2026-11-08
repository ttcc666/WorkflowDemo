import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";
afterEach(cleanup);
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};
HTMLElement.prototype.scrollIntoView ??= function () {}
HTMLElement.prototype.hasPointerCapture ??= function () { return false }
HTMLElement.prototype.setPointerCapture ??= function () {}
HTMLElement.prototype.releasePointerCapture ??= function () {}
window.matchMedia ??= ((query: string) => ({ matches: false, media: query, onchange: null,
  addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {}, dispatchEvent() { return false; },
})) as typeof window.matchMedia;
