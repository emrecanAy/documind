import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { linkifyCitations } from './citations'
import { MessageContent } from './MessageContent'

describe('citations', () => {
  it('turns [n] markers into citation links but leaves real links alone', () => {
    expect(linkifyCitations('Leave is 20 days [1][3].')).toBe('Leave is 20 days [1](#cite-1)[3](#cite-3).')
    expect(linkifyCitations('See [docs](https://x)')).toBe('See [docs](https://x)')
  })

  it('renders clickable citation chips', async () => {
    const onCite = vi.fn()
    render(<MessageContent content="Employees get **20 days** of leave [2]." onCite={onCite} />)

    expect(screen.getByText('20 days').tagName).toBe('STRONG')
    await userEvent.click(screen.getByRole('button', { name: 'Show source 2' }))
    expect(onCite).toHaveBeenCalledWith(2)
  })
})
