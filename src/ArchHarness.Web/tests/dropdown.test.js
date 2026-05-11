import { describe, expect, it, vi } from 'vitest';
import { buildDropdownMenuItems, createDropdown, createDropdownRegistry, updateDropdown } from '../wwwroot/js/dropdown.js';

describe('dropdown component', () => {
  it('builds accessible radio menu items and calls onSelect for enabled choices', () => {
    const menu = document.createElement('div');
    const onSelect = vi.fn();

    buildDropdownMenuItems(menu, [
      { value: 'standard', label: 'Standard' },
      { value: 'planning', label: 'Planning' }
    ], 'planning', onSelect);

    const items = [...menu.querySelectorAll('button')];
    expect(items).toHaveLength(2);
    expect(items[1].classList.contains('current')).toBe(true);
    expect(items[1].getAttribute('aria-checked')).toBe('true');

    items[0].click();
    expect(onSelect).toHaveBeenCalledWith('standard');
  });

  it('opens one dropdown at a time and closes the previous one', () => {
    const registry = createDropdownRegistry();
    const first = createDropdown('first', [{ value: 'a', label: 'A' }], 'a', { onSelect: vi.fn(), registry });
    const second = createDropdown('second', [{ value: 'b', label: 'B' }], 'b', { onSelect: vi.fn(), registry });
    document.body.append(first, second);

    first.querySelector('.composer-dropdown-button').click();
    expect(first.classList.contains('open')).toBe(true);
    expect(first.querySelector('.composer-dropdown-menu').classList.contains('hidden')).toBe(false);

    second.querySelector('.composer-dropdown-button').click();
    expect(first.classList.contains('open')).toBe(false);
    expect(second.classList.contains('open')).toBe(true);
  });

  it('selects a new value, updates label and dataset, then closes', () => {
    const registry = createDropdownRegistry();
    const onSelect = vi.fn();
    const dropdown = createDropdown('mode', [
      { value: 'standard', label: 'Standard' },
      { value: 'planning', label: 'Planning' }
    ], 'standard', { onSelect, registry });
    document.body.append(dropdown);

    dropdown.querySelector('.composer-dropdown-button').click();
    dropdown.querySelectorAll('.composer-dropdown-item')[1].click();

    expect(onSelect).toHaveBeenCalledWith('planning');
    expect(dropdown.dataset.value).toBe('planning');
    expect(dropdown.querySelector('.composer-dropdown-button span').textContent).toBe('Planning');
    expect(dropdown.classList.contains('open')).toBe(false);
  });

  it('disables the button when all choices are disabled and supports updates', () => {
    const registry = createDropdownRegistry();
    const dropdown = createDropdown('provider', [
      { value: 'none', label: 'No providers', disabled: true }
    ], 'none', { onSelect: vi.fn(), registry });

    expect(dropdown.querySelector('.composer-dropdown-button').disabled).toBe(true);

    updateDropdown(dropdown, [{ value: 'github', label: 'GitHub' }], 'github');
    expect(dropdown.querySelector('.composer-dropdown-button').disabled).toBe(false);
    expect(dropdown.dataset.value).toBe('github');
    expect(dropdown.querySelector('.composer-dropdown-button span').textContent).toBe('GitHub');
  });
});