import { TestBed } from '@angular/core/testing';
import { GravatarService } from './gravatar.service';

describe('GravatarService', () => {
  let service: GravatarService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(GravatarService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getGravatarUrl', () => {
    it('returns a Gravatar URL containing the MD5 hash of the lowercased trimmed email', () => {
      // Known MD5: 'test@example.com' -> '55502f40dc8b7c769880b10874abc9d0'
      const url = service.getGravatarUrl('test@example.com');
      expect(url).toContain('https://www.gravatar.com/avatar/55502f40dc8b7c769880b10874abc9d0');
      expect(url).toContain('d=mp');
      expect(url).toContain('s=80');
    });

    it('normalizes email casing and surrounding whitespace before hashing', () => {
      const a = service.getGravatarUrl('Test@Example.com');
      const b = service.getGravatarUrl('  test@example.com  ');
      expect(a).toBe(b);
    });

    it('honors the requested size parameter', () => {
      expect(service.getGravatarUrl('a@b.com', 200)).toContain('s=200');
    });
  });

  describe('getInitials', () => {
    it('returns first letters of first and last words, uppercased', () => {
      expect(service.getInitials('Phil Dieppa')).toBe('PD');
    });

    it('returns a single uppercase letter for a single-word name', () => {
      expect(service.getInitials('phil')).toBe('P');
    });

    it('returns empty string for empty input', () => {
      expect(service.getInitials('')).toBe('');
    });

    it('handles extra whitespace', () => {
      expect(service.getInitials('  john   doe  ')).toBe('JD');
    });
  });

  describe('getInitialsBackgroundColor', () => {
    it('returns a CSS hsl() string', () => {
      const color = service.getInitialsBackgroundColor('user@example.com');
      expect(color).toMatch(/^hsl\(\d{1,3}, 50%, 45%\)$/);
    });

    it('is deterministic - same input produces same output', () => {
      expect(service.getInitialsBackgroundColor('seed')).toBe(service.getInitialsBackgroundColor('seed'));
    });

    it('produces different colors for different seeds (in the typical case)', () => {
      const a = service.getInitialsBackgroundColor('alice@example.com');
      const b = service.getInitialsBackgroundColor('bob@example.com');
      expect(a).not.toBe(b);
    });
  });
});
