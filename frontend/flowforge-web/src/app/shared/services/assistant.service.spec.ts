import { TestBed } from '@angular/core/testing';
import { HttpDownloadProgressEvent, HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AssistantService } from './assistant.service';
import { AssistantError, AssistantStreamEvent } from '../models/assistant.models';

const sse = (payload: object) => `data: ${JSON.stringify(payload)}\n\n`;

describe('AssistantService', () => {
  let service: AssistantService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AssistantService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const progress = (partialText: string): HttpDownloadProgressEvent =>
    ({ type: HttpEventType.DownloadProgress, loaded: partialText.length, partialText } as HttpDownloadProgressEvent);

  it('turns server-sent events into typed events and converts snake_case to camelCase', () => {
    const events: AssistantStreamEvent[] = [];
    let completed = false;
    service.stream('who can invite?').subscribe({ next: e => events.push(e), complete: () => (completed = true) });

    const req = http.expectOne(r => r.url.endsWith('/ai/ask/stream'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ question: 'who can invite?', history: [] });

    const body =
      sse({ type: 'sources', sources: [{ document_id: 'd1', document_name: 'handbook.md', page: 2, content: 'Owners invite.', score: 0.8 }] }) +
      sse({ type: 'token', text: 'Only Owners ' }) +
      sse({ type: 'token', text: 'can invite [1].' }) +
      sse({ type: 'done', usage: { model: 'm', prompt_tokens: 10, completion_tokens: 5, cost_usd: 0.1, latency_ms: 900 } });

    req.event(progress(body));
    req.flush(body);

    expect(events.map(e => e.type)).toEqual(['sources', 'token', 'token', 'done']);
    const sources = events[0] as Extract<AssistantStreamEvent, { type: 'sources' }>;
    expect(sources.sources[0]).toEqual({ documentId: 'd1', documentName: 'handbook.md', page: 2, content: 'Owners invite.', score: 0.8 });
    const done = events[3] as Extract<AssistantStreamEvent, { type: 'done' }>;
    expect(done.usage).toEqual({ model: 'm', promptTokens: 10, completionTokens: 5, costUsd: 0.1, latencyMs: 900 });
    expect(completed).toBe(true);
  });

  it('sends the earlier conversation so a follow up can be understood', () => {
    const history = [
      { role: 'user' as const, content: 'Is he a fit?' },
      { role: 'assistant' as const, content: 'Probably [1].' }
    ];
    service.stream('why', history).subscribe();

    const req = http.expectOne(r => r.url.endsWith('/ai/ask/stream'));
    expect(req.request.body).toEqual({ question: 'why', history });
    req.flush('');
  });

  it('emits each event once even when the text arrives in pieces', () => {
    const tokens: string[] = [];
    service.stream('hi').subscribe(e => { if (e.type === 'token') tokens.push(e.text); });
    const req = http.expectOne(r => r.url.endsWith('/ai/ask/stream'));

    const first = sse({ type: 'token', text: 'Hello ' });
    const second = sse({ type: 'token', text: 'world' });
    const half = second.slice(0, 20); // an event cut in the middle must wait for its ending

    req.event(progress(first + half));
    expect(tokens).toEqual(['Hello ']);

    req.event(progress(first + second));
    expect(tokens).toEqual(['Hello ', 'world']);

    req.flush(first + second); // the final response must not replay what was already delivered
    expect(tokens).toEqual(['Hello ', 'world']);
  });

  it('ignores event types it does not know instead of failing the stream', () => {
    const types: string[] = [];
    service.stream('hi').subscribe(e => types.push(e.type));
    const req = http.expectOne(r => r.url.endsWith('/ai/ask/stream'));
    const body = sse({ type: 'something_new' }) + sse({ type: 'token', text: 'ok' });
    req.event(progress(body));
    req.flush(body);
    expect(types).toEqual(['token']);
  });

  describe('friendly errors', () => {
    const failWith = (status: number, body: unknown): AssistantError => {
      let caught!: AssistantError;
      service.stream('hi').subscribe({ error: e => (caught = e) });
      http.expectOne(r => r.url.endsWith('/ai/ask/stream'))
        .flush(typeof body === 'string' ? body : JSON.stringify(body), { status, statusText: 'err' });
      return caught;
    };

    it('shows the server message for 503 and 429', () => {
      expect(failWith(503, { detail: 'The assistant is temporarily unavailable. Please try again in a moment.' }).message)
        .toContain('temporarily unavailable');
      expect(failWith(429, { detail: 'The AI assistant is busy.' }).status).toBe(429);
    });

    it('explains a permission failure without leaking internals', () => {
      const err = failWith(403, '{}');
      expect(err).toBeInstanceOf(AssistantError);
      expect(err.message).toContain('owners and admins');
    });

    it('falls back to a generic sentence when the body is not JSON', () => {
      expect(failWith(500, '<html>boom</html>').message).toBe('Something went wrong. Please try again.');
    });
  });

  it('uploads a document as multipart form data', () => {
    const file = new File(['hello'], 'handbook.md', { type: 'text/markdown' });
    service.uploadDocument(file).subscribe();
    const req = http.expectOne(r => r.url.endsWith('/ai/documents'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).get('file')).toBeTruthy();
    req.flush({ documentId: 'd1', fileName: 'handbook.md', pages: 1, chunks: 1 });
  });
});
