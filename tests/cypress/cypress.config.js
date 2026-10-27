const { defineConfig } = require('cypress');

// Las pruebas necesitan la web, la API, PostgreSQL y Redis en marcha (ver README.md de esta carpeta).
module.exports = defineConfig({
  e2e: {
    baseUrl: process.env.MEDICORE_WEB_URL || 'http://localhost:5298',
    specPattern: 'e2e/**/*.cy.js',
    supportFile: 'support/e2e.js',
    video: false,
    screenshotsFolder: '../../reports/cypress/screenshots',
    trashAssetsBeforeRuns: true,
    defaultCommandTimeout: 8000,
    viewportWidth: 1280,
    viewportHeight: 800,
  },
  expose: {
    apiUrl: process.env.MEDICORE_API_URL || 'http://localhost:5011',
  },
});
