/* @vitest-environment jsdom */
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getAuditLogs } from './api'
import type { AuditLogPage } from './api'
import { AuditLogs } from './AuditLogs'

vi.mock('./api', () => ({
  ApiError: class ApiError extends Error {
    status: number
    constructor(status: number, message: string) {
      super(message)
      this.status = status
    }
  },
  getAuditLogs: vi.fn(),
}))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

const page: AuditLogPage = {
  items: [{
    id: 21,
    actorUserId: 7,
    actorName: 'Alex Admin',
    action: 'user.role_changed',
    entityType: 'User',
    entityId: 18,
    occurredAtUtc: '2026-10-28T12:30:00Z',
    details: '{"from":"Employee","to":"Manager"}',
  }],
  page: 1,
  pageSize: 25,
  totalCount: 1,
}

describe('AuditLogs', () => {
  it('loads audit events and applies action, entity, and date filters', async () => {
    vi.mocked(getAuditLogs).mockResolvedValue(page)
    const user = userEvent.setup()
    render(<AuditLogs />)

    expect(await screen.findByText('user.role_changed')).toBeTruthy()
    expect(screen.getByText('Alex Admin')).toBeTruthy()
    expect(screen.getByText('User #18')).toBeTruthy()

    await user.type(screen.getByLabelText('Action'), 'request.approved')
    await user.type(screen.getByLabelText('Entity'), 'ApprovalRequest')
    fireEvent.change(screen.getByLabelText('From'), {
      target: { value: '2026-10-01' },
    })
    fireEvent.change(screen.getByLabelText('To'), {
      target: { value: '2026-10-28' },
    })
    await user.click(screen.getByRole('button', { name: 'Apply filters' }))

    await waitFor(() => {
      expect(getAuditLogs).toHaveBeenLastCalledWith({
        page: 1,
        pageSize: 25,
        action: 'request.approved',
        entityType: 'ApprovalRequest',
        fromUtc: '2026-10-01T00:00:00.000Z',
        toUtc: '2026-10-28T23:59:59.999Z',
      })
    })
  })

  it('shows empty, loading, and error states', async () => {
    let resolveRequest: ((value: AuditLogPage) => void) | undefined
    vi.mocked(getAuditLogs).mockReturnValue(new Promise((resolve) => {
      resolveRequest = resolve
    }))
    render(<AuditLogs />)
    expect(screen.getByRole('status').textContent).toContain('Loading')

    resolveRequest?.({ ...page, items: [], totalCount: 0 })
    expect(await screen.findByText('No audit records match these filters.'))
      .toBeTruthy()

    vi.mocked(getAuditLogs).mockRejectedValue(new Error('Audit service unavailable'))
    await userEvent.setup().click(screen.getByRole('button', {
      name: 'Apply filters',
    }))
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByText(
      'Audit history could not be loaded. Please try again.',
    )).toBeTruthy()
  })
})
