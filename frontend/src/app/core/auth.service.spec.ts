import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('sends traders to the user dashboard after login', () => {
    expect(
      service.homeRouteForUser({
        id: '1',
        email: 't@example.com',
        displayName: 'Trader',
        role: 'Trader',
      }),
    ).toBe('/dashboard');
  });

  it('sends admins to the admin console after login', () => {
    expect(
      service.homeRouteForUser({
        id: '1',
        email: 'a@example.com',
        displayName: 'Admin',
        role: 'Admin',
      }),
    ).toBe('/admin');
  });
});