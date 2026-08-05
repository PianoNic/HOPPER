export const environment = {
  production: true,
  // In production the API serves the built SPA out of its own wwwroot, so the API is
  // always same-origin and there is nothing to configure at build time.
  apiBaseUrl: window.location.origin,
};
