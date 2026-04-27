/** Manifest returned by GET /api/extensions */
export interface ExtensionManifest {
  name: string;
  scriptUrl: string;
  pages: ExtensionPage[];
}

export interface ExtensionPage {
  path: string;
  label: string;
  icon?: string;
}

/** The API exposed to extension install() functions via the warp parameter */
export interface WarpExtensionAPI {
  /** Replace the contents of elements matching the selector */
  mount(selector: string, component: React.ComponentType<ExtensionProps>): void;
  /** Append a component inside elements matching the selector */
  append(selector: string, component: React.ComponentType<ExtensionProps>): void;
  /** Insert a component before elements matching the selector */
  insertBefore(selector: string, component: React.ComponentType<ExtensionProps>): void;
  /** Insert a component after elements matching the selector */
  insertAfter(selector: string, component: React.ComponentType<ExtensionProps>): void;
  /** Register a new page with optional nav item */
  addPage(config: { path: string; label: string; icon?: string; component: React.ComponentType }): void;
}

/** Props passed to extension components mounted via DOM slots */
export interface ExtensionProps {
  [key: string]: unknown;
}

/** What an extension JS module exports */
export interface ExtensionModule {
  install(warp: WarpExtensionAPI): void;
}
