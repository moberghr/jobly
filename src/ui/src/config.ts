declare global {
  interface Window {
    apiPath?: string;
    basePath?: string;
    hasBuiltInLogin?: boolean;
  }
}

export const config = {
  apiPath: window.apiPath || '/warp/api/',
  basePath: window.basePath || '/warp',
  hasBuiltInLogin: window.hasBuiltInLogin === true,
};
