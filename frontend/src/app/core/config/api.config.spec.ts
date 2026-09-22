import { buildApiUrl, isApiUrl } from './api.config';

describe('api.config', () => {
  describe('buildApiUrl', () => {
    it.each([
      ['http://api.test', 'health'],
      ['http://api.test/', '/health'],
      ['http://api.test//', '//health'],
    ])('joins %s and %s with a single slash', (baseUrl, path) => {
      expect(buildApiUrl({ baseUrl }, path)).toBe('http://api.test/health');
    });

    it('supports same-origin relative base URLs', () => {
      expect(buildApiUrl({ baseUrl: '/api' }, 'health')).toBe('/api/health');
    });
  });

  describe('isApiUrl', () => {
    const config = { baseUrl: 'http://api.test' };

    it('matches URLs under the base URL', () => {
      expect(isApiUrl(config, 'http://api.test/work-orders')).toBe(true);
      expect(isApiUrl(config, 'http://api.test')).toBe(true);
    });

    it('rejects other origins and prefix look-alikes', () => {
      expect(isApiUrl(config, 'https://third-party.test/x')).toBe(false);
      expect(isApiUrl(config, 'http://api.test.evil.test/x')).toBe(false);
    });
  });
});
