/**
 * Shared dropdown component.
 *
 * Two usage modes:
 *
 * 1. Dynamic (settings): use createDropdown / updateDropdown / createDropdownRegistry
 *    to create self-contained dropdown elements with their own open-state management.
 *
 * 2. Render-into-existing-DOM (composer): use buildDropdownMenuItems to populate
 *    a pre-existing <div role="menu"> from an array of options.
 */

/**
 * Populates a menu element with radio-style menu items.
 * Shared by both composer and dynamic dropdowns.
 *
 * @param {HTMLElement} menu - The container to populate.
 * @param {{ value: string, label: string, disabled?: boolean }[]} options
 * @param {string} currentValue - The currently selected value.
 * @param {(value: string) => void} onSelect - Called when an item is clicked.
 */
export function buildDropdownMenuItems(menu, options, currentValue, onSelect) {
  menu.replaceChildren();
  options.forEach(opt => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = "composer-dropdown-item";
    item.setAttribute("role", "menuitemradio");
    item.setAttribute("aria-checked", opt.value === currentValue ? "true" : "false");
    item.classList.toggle("current", opt.value === currentValue);
    item.textContent = opt.label;
    item.disabled = !!opt.disabled;
    item.addEventListener("click", event => {
      event.stopPropagation();
      onSelect(opt.value);
    });
    menu.append(item);
  });
}

/**
 * Creates a scoped open-state registry for a group of dynamic dropdowns.
 * Each registry is independent, so composer and settings don't interfere.
 *
 * @returns {{ isOpen: (id: string) => boolean, open: (wrap: HTMLElement, id: string) => void, close: () => void }}
 */
export function createDropdownRegistry() {
  let current = null;

  function close() {
    current = null;
    document.querySelectorAll(".dd-registry-open").forEach(el => {
      el.classList.remove("open", "dd-registry-open");
      el.querySelector(".composer-dropdown-menu")?.classList.add("hidden");
      el.querySelector(".composer-dropdown-button")?.setAttribute("aria-expanded", "false");
    });
  }

  function open(wrap, id) {
    close();
    current = id;
    wrap.classList.add("open", "dd-registry-open");
    wrap.querySelector(".composer-dropdown-menu")?.classList.remove("hidden");
    wrap.querySelector(".composer-dropdown-button")?.setAttribute("aria-expanded", "true");
  }

  function isOpen(id) {
    return current === id;
  }

  return { isOpen, open, close };
}

/**
 * Creates a fully self-contained dropdown element.
 *
 * @param {string} id - Unique identifier; stored as data-dropdown-id on the wrapper.
 * @param {{ value: string, label: string, disabled?: boolean }[]} options
 * @param {string} selectedValue
 * @param {{ onSelect: (value: string) => void, registry: ReturnType<typeof createDropdownRegistry>, extraClass?: string }} config
 * @returns {HTMLElement} The wrapper div.
 */
export function createDropdown(id, options, selectedValue, { onSelect, registry, extraClass = "" }) {
  const wrap = document.createElement("div");
  wrap.className = ["composer-dropdown", extraClass].filter(Boolean).join(" ");
  wrap.dataset.dropdownId = id;
  wrap.dataset.value = selectedValue || "";

  const button = document.createElement("button");
  button.type = "button";
  button.className = "composer-dropdown-button";
  button.setAttribute("aria-haspopup", "menu");
  button.setAttribute("aria-expanded", "false");

  const labelSpan = document.createElement("span");
  labelSpan.textContent = options.find(o => o.value === selectedValue)?.label ?? selectedValue ?? "";

  const chevron = document.createElement("i");
  chevron.className = "fa-solid fa-chevron-down";
  chevron.setAttribute("aria-hidden", "true");
  button.append(labelSpan, chevron);

  const menu = document.createElement("div");
  menu.className = "composer-dropdown-menu hidden";
  menu.setAttribute("role", "menu");

  const hasChoices = options.some(o => !o.disabled);
  button.disabled = !hasChoices;

  function handleSelect(value) {
    const opt = options.find(o => o.value === value);
    wrap.dataset.value = value;
    labelSpan.textContent = opt?.label ?? value ?? "";
    // Refresh aria states on all items
    menu.querySelectorAll(".composer-dropdown-item").forEach(item => {
      item.classList.toggle("current", item.dataset.value === value);
      item.setAttribute("aria-checked", item.dataset.value === value ? "true" : "false");
    });
    onSelect(value);
    registry.close();
  }

  // Rebuild menu items, tagging each with their value for the aria refresh above
  function buildItems(opts, val) {
    options = opts;
    menu.replaceChildren();
    opts.forEach(opt => {
      const item = document.createElement("button");
      item.type = "button";
      item.className = "composer-dropdown-item";
      item.dataset.value = opt.value;
      item.setAttribute("role", "menuitemradio");
      item.setAttribute("aria-checked", opt.value === val ? "true" : "false");
      item.classList.toggle("current", opt.value === val);
      item.textContent = opt.label;
      item.disabled = !!opt.disabled;
      item.addEventListener("click", event => {
        event.stopPropagation();
        handleSelect(opt.value);
      });
      menu.append(item);
    });
  }

  buildItems(options, selectedValue);

  button.addEventListener("click", event => {
    event.stopPropagation();
    if (registry.isOpen(id)) {
      registry.close();
    } else if (hasChoices) {
      registry.open(wrap, id);
    }
  });

  wrap.append(button, menu);

  // Expose update API on the element itself for updateDropdown()
  wrap._update = (newOptions, newValue) => {
    options = newOptions;
    const newHasChoices = newOptions.some(o => !o.disabled);
    button.disabled = !newHasChoices;
    wrap.dataset.value = newValue || "";
    const newOpt = newOptions.find(o => o.value === newValue);
    labelSpan.textContent = newOpt?.label ?? newValue ?? "";
    buildItems(newOptions, newValue);
  };

  return wrap;
}

/**
 * Updates the options and selected value of a dropdown created with createDropdown.
 *
 * @param {HTMLElement} wrap - The wrapper element returned by createDropdown.
 * @param {{ value: string, label: string, disabled?: boolean }[]} options
 * @param {string} value
 */
export function updateDropdown(wrap, options, value) {
  wrap._update?.(options, value);
}
