import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SystemMonitoringService } from './system-monitoring.service';

describe('SystemMonitoringService', () => {
  let service: SystemMonitoringService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SystemMonitoringService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SystemMonitoringService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads live snapshot', async () => {
    const promise = firstValueFrom(service.getLiveSnapshot());
    const req = httpMock.expectOne('/api/admin/system-health/live');
    expect(req.request.method).toBe('GET');
    req.flush({ overallStatus: 'Healthy', instanceId: 'a', apiStatus: 'ok', database: {}, workers: [], runtime: {} });
    const snap = await promise;
    expect(snap.overallStatus).toBe('Healthy');
  });

  it('acknowledges incident', async () => {
    const promise = firstValueFrom(service.acknowledgeIncident('abc'));
    const req = httpMock.expectOne('/api/admin/incidents/abc/acknowledge');
    expect(req.request.method).toBe('POST');
    req.flush({ id: 'abc', status: 'Acknowledged' });
    await promise;
  });

  it('updates notification settings without secrets', async () => {
    const promise = firstValueFrom(
      service.updateNotificationSettings({ notificationsEnabled: true, primaryEmailRecipients: ['ops@example.com'] }),
    );
    const req = httpMock.expectOne('/api/admin/notification-settings');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.primaryEmailRecipients).toEqual(['ops@example.com']);
    expect(req.request.body.smtpPassword).toBeUndefined();
    req.flush({ notificationsEnabled: true, primaryEmailRecipients: ['ops@example.com'] });
    await promise;
  });
});