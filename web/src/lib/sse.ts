export interface SseEvent {
  event: string
  data: string
}

/**
 * Incremental Server-Sent Events parser for fetch() streams.
 * (EventSource cannot send POST bodies or Authorization headers, so we read the stream ourselves.)
 */
export async function* readSse(body: ReadableStream<Uint8Array>, signal?: AbortSignal): AsyncGenerator<SseEvent> {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  try {
    while (true) {
      if (signal?.aborted) return
      const { value, done } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n')

      let boundary = buffer.indexOf('\n\n')
      while (boundary >= 0) {
        const block = buffer.slice(0, boundary)
        buffer = buffer.slice(boundary + 2)
        const parsed = parseBlock(block)
        if (parsed) yield parsed
        boundary = buffer.indexOf('\n\n')
      }
    }
    const tail = parseBlock(buffer + decoder.decode())
    if (tail) yield tail
  } finally {
    reader.releaseLock()
  }
}

export function parseBlock(block: string): SseEvent | null {
  let event = 'message'
  const data: string[] = []
  for (const line of block.split('\n')) {
    if (line.startsWith('event:')) event = line.slice(6).trim()
    else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''))
  }
  return data.length ? { event, data: data.join('\n') } : null
}
