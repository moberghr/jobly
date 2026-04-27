import React from 'react';
import ReactDOM from 'react-dom/client';
import type { ExtensionProps, WarpExtensionAPI } from './types';

type MountMode = 'mount' | 'append' | 'insertBefore' | 'insertAfter';

interface Registration {
  selector: string;
  component: React.ComponentType<ExtensionProps>;
  mode: MountMode;
}

interface ActiveMount {
  root: ReactDOM.Root;
  container: HTMLElement;
  element: HTMLElement;
}

interface PageRegistration {
  path: string;
  label: string;
  icon?: string;
  component: React.ComponentType;
}

export class ExtensionRuntime {
  private registrations: Registration[] = [];
  private activeMounts: ActiveMount[] = [];
  private pages: PageRegistration[] = [];
  private observer: MutationObserver | null = null;

  /** Create the extension API that gets passed to install() */
  createAPI(): WarpExtensionAPI {
    return {
      mount: (selector, component) => this.register(selector, component, 'mount'),
      append: (selector, component) => this.register(selector, component, 'append'),
      insertBefore: (selector, component) => this.register(selector, component, 'insertBefore'),
      insertAfter: (selector, component) => this.register(selector, component, 'insertAfter'),
      addPage: (config) => this.pages.push(config),
    };
  }

  /** Get all registered extension pages */
  getPages(): PageRegistration[] {
    return this.pages;
  }

  /** Start observing the DOM for slot elements */
  start(): void {
    // Process any elements already in the DOM
    this.processAll();

    // Watch for future DOM changes (SPA navigation, lazy loading)
    this.observer = new MutationObserver(() => this.processAll());
    this.observer.observe(document.body, {
      childList: true,
      subtree: true,
    });
  }

  /** Stop observing and unmount all extension roots */
  stop(): void {
    this.observer?.disconnect();
    this.observer = null;
    for (const mount of this.activeMounts) {
      mount.root.unmount();
      mount.container.remove();
    }
    this.activeMounts = [];
  }

  private register(selector: string, component: React.ComponentType<ExtensionProps>, mode: MountMode): void {
    this.registrations.push({ selector, component, mode });
  }

  private processAll(): void {
    // Clean up mounts whose target element was removed from the DOM
    this.activeMounts = this.activeMounts.filter((mount) => {
      if (!document.body.contains(mount.element)) {
        mount.root.unmount();
        mount.container.remove();
        return false;
      }
      return true;
    });

    // Process each registration
    for (const reg of this.registrations) {
      const elements = document.querySelectorAll<HTMLElement>(reg.selector);
      for (const element of elements) {
        // Skip if already mounted on this element for this registration
        if (this.activeMounts.some((m) => m.element === element && m.container.dataset.extReg === regKey(reg))) {
          continue;
        }

        this.mountOn(element, reg);
      }
    }
  }

  private mountOn(element: HTMLElement, reg: Registration): void {
    // Parse context from data-context attribute
    const contextStr = element.dataset.warpContext;
    let context: Record<string, unknown> = {};
    if (contextStr) {
      try {
        context = JSON.parse(contextStr);
      } catch {
        // ignore parse errors
      }
    }

    const container = document.createElement('div');
    container.dataset.extReg = regKey(reg);

    switch (reg.mode) {
      case 'mount':
        // Hide original children, render extension inside
        for (const child of Array.from(element.children)) {
          (child as HTMLElement).style.display = 'none';
        }
        element.appendChild(container);
        break;

      case 'append':
        // Add inside the element at the end
        element.appendChild(container);
        break;

      case 'insertBefore':
        // Add as a sibling before the element
        element.parentNode?.insertBefore(container, element);
        break;

      case 'insertAfter':
        // Add as a sibling after the element
        element.parentNode?.insertBefore(container, element.nextSibling);
        break;
    }

    const root = ReactDOM.createRoot(container);
    root.render(React.createElement(reg.component, context));

    this.activeMounts.push({ root, container, element });
  }
}

function regKey(reg: Registration): string {
  return `${reg.mode}:${reg.selector}`;
}

/** Singleton runtime instance */
export const extensionRuntime = new ExtensionRuntime();
