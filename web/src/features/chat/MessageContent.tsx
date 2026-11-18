import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { cn } from '@/lib/utils'
import { linkifyCitations } from './citations'

export function MessageContent({
  content,
  activeCitation,
  onCite,
}: {
  content: string
  activeCitation?: number | null
  onCite: (n: number) => void
}) {
  return (
    <div className="prose-answer text-[15px] text-slate-800">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          a: ({ href, children }) => {
            if (href?.startsWith('#cite-')) {
              const n = Number(href.slice(6))
              return (
                <button
                  type="button"
                  onClick={() => onCite(n)}
                  className={cn(
                    'mx-0.5 inline-flex h-5 min-w-5 -translate-y-0.5 items-center justify-center rounded-md px-1 text-[11px] font-semibold transition',
                    activeCitation === n ? 'bg-brand-600 text-white' : 'bg-brand-100 text-brand-700 hover:bg-brand-200',
                  )}
                  aria-label={`Show source ${n}`}
                >
                  {n}
                </button>
              )
            }
            return (
              <a href={href} target="_blank" rel="noreferrer" className="text-brand-600 underline">
                {children}
              </a>
            )
          },
        }}
      >
        {linkifyCitations(content)}
      </ReactMarkdown>
    </div>
  )
}
