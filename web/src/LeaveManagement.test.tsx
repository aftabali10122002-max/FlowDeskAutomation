/* @vitest-environment jsdom */
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LeaveManagement } from './LeaveManagement'

function response(status: number, data: unknown) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('My Leave', () => {
  const requests: { path: string; method: string; body?: string }[] = []
  let leaveRequests: unknown[] = []

  beforeEach(() => {
    requests.length = 0
    leaveRequests = []
    sessionStorage.setItem('flowdesk-token', 'test-token')
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      const method = init?.method ?? 'GET'
      requests.push({
        path: url.pathname,
        method,
        ...(init?.body ? { body: String(init.body) } : {}),
      })
      if (url.pathname === '/api/leave/me/balances') {
        return response(200, [{
          employeeUserId: 8,
          leaveTypeId: 2,
          leaveTypeCode: 'ANNUAL',
          leaveTypeName: 'Annual Leave',
          earned: 12,
          adjustments: 0,
          carryForward: 2,
          used: 3,
          pending: 1,
          scheduled: 2,
          expired: 0,
          available: 8,
        }])
      }
      if (url.pathname === '/api/leave/me/history') return response(200, [])
      if (url.pathname === '/api/leave/types') {
        return response(200, [{
          id: 2,
          code: 'ANNUAL',
          displayName: 'Annual Leave',
          isPaid: true,
          isEnabled: true,
        }])
      }
      if (url.pathname === '/api/requests' && method === 'GET') return response(200, leaveRequests)
      if (url.pathname === '/api/leave/me/preview') {
        return response(200, {
          totalUnits: 0.5,
          days: [{ date: '2026-11-02', units: 0.5, policyId: 12, policyVersion: 2 }],
        })
      }
      if (url.pathname === '/api/leave/manager/team') return response(200, [])
      if (url.pathname === '/api/requests' && method === 'POST') {
        return response(201, { id: 44 })
      }
      return response(404, { message: 'Not found' })
    }))
  })

  afterEach(() => {
    cleanup()
    sessionStorage.clear()
    vi.unstubAllGlobals()
  })

  it('shows server balances, previews policy-calculated leave, and submits half-day selections', async () => {
    render(
      <LeaveManagement
        role="Employee"
        onOpenRequest={vi.fn()}
      />,
    )

    expect(await screen.findByText('My Leave')).toBeTruthy()
    expect(screen.getAllByText('Annual Leave').length).toBeGreaterThan(0)
    expect(screen.getByText('Available leave').closest('article')?.textContent).toContain('8')

    fireEvent.change(screen.getByLabelText('Start date'), {
      target: { value: '2026-11-02' },
    })
    fireEvent.change(screen.getByLabelText('End date'), {
      target: { value: '2026-11-02' },
    })
    fireEvent.click(screen.getByLabelText('First day is a half day'))
    fireEvent.change(screen.getByLabelText('Reason'), {
      target: { value: 'Personal appointment' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Calculate preview' }))

    expect(await screen.findByText('0.5 leave days')).toBeTruthy()
    expect(requests.some((entry) => entry.path === '/api/leave/me/preview')).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'Submit leave request' }))
    await waitFor(() => {
      expect(requests.some((entry) =>
        entry.path === '/api/requests' && entry.method === 'POST')).toBe(true)
    })
    const submitted = requests.find((entry) =>
      entry.path === '/api/requests' && entry.method === 'POST')
    expect(JSON.parse(submitted?.body ?? '{}')).toMatchObject({
      requestTypeCode: 'LEAVE',
      leave: {
        leaveType: 'ANNUAL',
        startDayPortion: 'HalfDay',
        endDayPortion: 'FullDay',
      },
    })
  })

  describe('Admin Leave Management', () => {
    const schedule = {
      id: 1,
      code: 'QA-WEEK',
      name: 'QA Work Schedule',
      isEnabled: true,
      days: [
        { dayOfWeek: 0, isWorkingDay: false, standardHours: 0 },
        { dayOfWeek: 1, isWorkingDay: true, standardHours: 8 },
        { dayOfWeek: 2, isWorkingDay: true, standardHours: 8 },
        { dayOfWeek: 3, isWorkingDay: true, standardHours: 8 },
        { dayOfWeek: 4, isWorkingDay: true, standardHours: 8 },
        { dayOfWeek: 5, isWorkingDay: true, standardHours: 8 },
        { dayOfWeek: 6, isWorkingDay: false, standardHours: 0 },
      ],
    }

    beforeEach(() => {
      sessionStorage.setItem('flowdesk-token', 'test-token')
    })

    afterEach(() => {
      cleanup()
      sessionStorage.clear()
      vi.unstubAllGlobals()
    })

    async function openWorkSchedules(schedules: unknown[]) {
      vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
        const { pathname } = new URL(String(input), 'http://localhost')
        if (pathname === '/api/admin/leave/types') return response(200, [])
        if (pathname === '/api/admin/leave/policies') return response(200, [])
        if (pathname === '/api/admin/leave/work-schedules') return response(200, schedules)
        if (pathname === '/api/admin/leave/holiday-calendars') return response(200, [])
        if (pathname === '/api/admin/leave/payroll-calendars') return response(200, [])
        if (pathname === '/api/admin/leave/designations') return response(200, [])
        return response(404, { message: 'Not found' })
      }))

      render(
        <LeaveManagement
          role="Admin"
          users={[{
            id: 1,
            fullName: 'QA Admin',
            email: 'admin@example.test',
            role: 'Admin',
            isActive: true,
            departmentId: null,
            createdAtUtc: '2026-01-01T00:00:00Z',
          }]}
          departments={[{
            id: 1,
            name: 'QA',
            code: 'QA',
            isActive: true,
            createdAtUtc: '2026-01-01T00:00:00Z',
          }]}
          onOpenRequest={vi.fn()}
        />,
      )

      await screen.findByRole('heading', { name: 'Leave Management' })
      fireEvent.click(screen.getByRole('button', { name: 'Work Schedules' }))
      await screen.findByRole('heading', { name: 'Work schedules' })
    }

    it('shows the schedule form and existing schedules when the tab is selected', async () => {
      await openWorkSchedules([schedule])

      expect(screen.getByLabelText('Code')).toBeTruthy()
      expect(screen.getByLabelText('Name')).toBeTruthy()
      expect(screen.getByLabelText('Hours per working day')).toBeTruthy()
      expect(screen.getByText('QA Work Schedule')).toBeTruthy()
      expect(screen.getByText('Mon, Tue, Wed, Thu, Fri')).toBeTruthy()
    })

    it('explains when no work schedules exist yet', async () => {
      await openWorkSchedules([])

      expect(screen.getByText(
        'No work schedules yet. Create one above to use in working-day leave policies.',
      )).toBeTruthy()
    })
  })

  it('does not show an approved cancellation as upcoming leave', async () => {
    const futureDate = '2099-12-25'
    leaveRequests = [
      {
        id: 114,
        title: 'Annual leave request',
        status: 'Cancelled',
        requestTypeCode: 'LEAVE',
        requestDetails: {
          leaveType: 'ANNUAL',
          startDate: futureDate,
          endDate: futureDate,
        },
      },
      {
        id: 115,
        title: 'Cancellation for REQ-0114',
        status: 'Approved',
        requestTypeCode: 'LEAVE',
        requestDetails: {
          leaveType: 'ANNUAL',
          startDate: futureDate,
          endDate: futureDate,
          cancellationOfRequestId: 114,
        },
      },
      {
        id: 116,
        title: 'Sick leave request',
        status: 'Approved',
        requestTypeCode: 'LEAVE',
        requestDetails: {
          leaveType: 'SICK',
          startDate: futureDate,
          endDate: futureDate,
        },
      },
    ]

    render(
      <LeaveManagement
        role="Employee"
        onOpenRequest={vi.fn()}
      />,
    )

    const upcoming = await screen.findAllByRole('listitem')
    expect(upcoming).toHaveLength(1)
    expect(upcoming[0].textContent).toContain('SICK')
    expect(upcoming[0].textContent).not.toContain('Annual')
  })

  it('shows a helpful empty state when the authorized team has no leave balances', async () => {
    render(
      <LeaveManagement
        role="Manager"
        onOpenRequest={vi.fn()}
      />,
    )

    expect(await screen.findByText('Team Leave')).toBeTruthy()
    expect(await screen.findByText('No team leave balances are available.')).toBeTruthy()
    expect(screen.getByText('No upcoming approved leave is visible to your role.')).toBeTruthy()
  })
})
