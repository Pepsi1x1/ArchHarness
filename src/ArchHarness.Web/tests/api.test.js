import { beforeEach, describe, expect, it, vi } from 'vitest';
import { requestEventStream, requestJson } from '../wwwroot/js/api.js';

function jsonResponse(body, init = {}) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'content-type': 'application/json' },
    ...init
  });
}

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn());
});

describe('api helpers', () => {
  it('returns parsed JSON for successful requests', async () => {
    fetch.mockResolvedValueOnce(jsonResponse({ ok: true }));

    await expect(requestJson('/api/test')).resolves.toEqual({ ok: true });
    expect(fetch).toHaveBeenCalledWith('/api/test', undefined);
  });

  it('returns null for 204 and parses optional 202 JSON bodies', async () => {
    fetch
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse({ accepted: true }, { status: 202 }));

    await expect(requestJson('/api/no-content')).resolves.toBeNull();
    await expect(requestJson('/api/accepted')).resolves.toEqual({ accepted: true });
  });

  it('throws useful errors with status and payload data', async () => {
    fetch.mockResolvedValueOnce(jsonResponse({ error: 'No pending request' }, { status: 409 }));

    await expect(requestJson('/api/conflict')).rejects.toMatchObject({
      message: 'No pending request',
      status: 409,
      data: { error: 'No pending request' }
    });
  });

  it('parses server-sent event blocks and JSON data payloads', async () => {
    const encoder = new TextEncoder();
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode(': keepalive\n\nevent: agent-delta\ndata: {"message":"hello"}\n\nevent: plain\ndata: text\n\n'));
        controller.close();
      }
    });
    fetch.mockResolvedValueOnce(new Response(stream, { status: 200 }));
    const events = [];

    await requestEventStream('/api/events', { onEvent: event => events.push(event) });

    expect(events).toEqual([
      { event: 'agent-delta', data: { message: 'hello' } },
      { event: 'plain', data: 'text' }
    ]);
  });
});