declare global {
  interface Window {
    apiPath?: string;
    basePath?: string;
    hasBuiltInLogin?: boolean;
  }
}

export const config = {
  apiPath: window.apiPath || '/jobly/api/',
  basePath: window.basePath || '/jobly',
  hasBuiltInLogin: window.hasBuiltInLogin === true,
};
