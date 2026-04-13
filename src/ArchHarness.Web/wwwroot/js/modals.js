import { state, elements } from './state.js';

const preCloseHandlers = [];

export function registerModalPreClose(modalIdOrHandler, handler) {
  if (typeof modalIdOrHandler === "function") {
    preCloseHandlers.push(modalIdOrHandler);
  } else {
    const targetModalId = modalIdOrHandler;
    preCloseHandlers.push((openModalId, options) => {
      if (openModalId === targetModalId) {
        return handler(openModalId, options);
      }
    });
  }
}

export function openModal(modalId) {
  closeModal();
  const modal = document.getElementById(modalId);
  if (!modal) {
    return;
  }

  state.openModalId = modalId;
  modal.classList.remove("hidden");
  modal.setAttribute("aria-hidden", "false");
  elements.modalBackdrop.classList.remove("hidden");
}

export function closeModal(options = {}) {
  let afterCloseCallback = null;

  for (const handler of preCloseHandlers) {
    const result = handler(state.openModalId, options);
    if (result?.afterClose) {
      afterCloseCallback = result.afterClose;
    }
  }

  if (!state.openModalId) {
    elements.modalBackdrop.classList.add("hidden");
    return;
  }

  const modal = document.getElementById(state.openModalId);
  if (modal) {
    modal.classList.add("hidden");
    modal.setAttribute("aria-hidden", "true");
  }

  state.openModalId = null;
  elements.modalBackdrop.classList.add("hidden");

  if (typeof afterCloseCallback === "function") {
    afterCloseCallback();
  }
}
