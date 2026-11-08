export function revealTab(tab: HTMLElement) {
  const strip = tab.closest<HTMLElement>("[data-tab-scroll]");
  if (!strip) return;
  const bounds = strip.getBoundingClientRect();
  const target = tab.getBoundingClientRect();
  if (target.left < bounds.left) strip.scrollLeft -= bounds.left - target.left;
  else if (target.right > bounds.right)
    strip.scrollLeft += target.right - bounds.right;
}
