import { parseBlock, readSse } from './sse'

function streamOf(...chunks: string[]) {
  const encoder = new TextEncoder()
  return new ReadableStream<Uint8Array>({
    start(controller) {
      chunks.forEach((c) => controller.enqueue(encoder.encode(c)))
      controller.close()
    },
  })
}

describe('SSE parser', () => {
  it('parses event name and data', () => {
    expect(parseBlock('event: delta\ndata: {"text":"hi"}')).toEqual({ event: 'delta', data: '{"text":"hi"}' })
  })

  it('defaults the event name to "message" and ignores comments', () => {
    expect(parseBlock(': keep-alive\ndata: 1')).toEqual({ event: 'message', data: '1' })
    expect(parseBlock(': keep-alive')).toBeNull()
  })

  it('reassembles events split across network chunks', async () => {
    const events = []
    for await (const e of readSse(streamOf('event: sta', 'tus\ndata: {"a":1}\n\nevent: delta\r\n', 'data: {"text":"x"}\r\n\r\n'))) {
      events.push(e)
    }
    expect(events).toEqual([
      { event: 'status', data: '{"a":1}' },
      { event: 'delta', data: '{"text":"x"}' },
    ])
  })
})
