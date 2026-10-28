/* @vitest-environment jsdom */
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { approvalProgressColor, priorityColor, requestTypeColor } from './DashboardAnalyticsColors'
import { formatDate } from './dateFormatting'

const signalrMock = vi.hoisted(() => ({
  handlers: {} as Record<string, (value?: unknown) => void>,
}))

vi.mock('@microsoft/signalr', ()  => {
  const connection = {
    start: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined),
    on: (event: string, handler: (value?: unknown) => void) => {
      signalrMock.handlers[event] = handler
    },
    onreconnecting: (handler: () => void) => {
      signalrMock.handlers.reconnecting = handler
    },
    onreconnected: (handler: () => void) => {
      signalrMock.handlers.reconnected = handler
    },
    onclose: (handler: () => void) => {
      signalrMock.handlers.close = handler
    },
  }
  class MockHubConnectionBuilder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { return connection }
  }
  return {
    HubConnectionBuilder: MockHubConnectionBuilder,
    LogLevel: { Warning: 3 },
  }
})

describe('date formatting', () => {
  it('preserves date-only values across time zones', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles')
    try {
      expect(formatDate('2026-11-02')).toBe('Nov 2, 2026')
      expect(formatDate('2026-10-26T19:51:19Z')).toBeTruthy()
    } finally {
      vi.unstubAllEnvs()
    }
  })
})

const pendingRequest = {
  id: 317,
  title: 'Laptop replacement',
  description: 'Required for the design team.',
  amount: 1250,
  status: 'Pending',
  createdAtUtc: '2026-10-19T12:00:00Z',
  submittedBy: 'Taylor Employee',
  department: 'Operations',
  departmentId: 7,
  assignments: [{
    id: 901,
    approverName: 'Ali Manager',
    status: 'Pending',
    assignedAtUtc: '2026-10-19T13:00:00Z',
    decidedAtUtc: null,
  }],
}

const requestTypes = [
  { id: 1, code: 'MATERIAL_PURCHASE', displayName: 'Material Purchase', requiresAmount: true, isEnabled: true },
  { id: 2, code: 'LEAVE', displayName: 'Leave Request', requiresAmount: false, isEnabled: true },
  { id: 3, code: 'EXPENSE_REIMBURSEMENT', displayName: 'Expense Reimbursement', requiresAmount: true, isEnabled: true },
  { id: 4, code: 'IT_ACCESS', displayName: 'IT Access', requiresAmount: false, isEnabled: true },
  { id: 5, code: 'TRAVEL', displayName: 'Travel', requiresAmount: true, isEnabled: true },
  { id: 6, code: 'WORK_FROM_HOME', displayName: 'Work From Home', requiresAmount: false, isEnabled: true },
]

type MockRequest = Omit<typeof pendingRequest, 'description' | 'amount'> & {
  description?: string | null
  amount: number | null
  priority?: 'Low' | 'Medium' | 'High' | 'Urgent'
  requestTypeCode?: string | null
  requestTypeName?: string | null
  requestDetails?: {
    leaveType?: string
    startDate?: string
    endDate?: string
    reason?: string
    totalLeaveDays?: number
    calculatedLeaveUnits?: number
  } | null
}

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function setViewportWidth(width: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: width })
}

function mockApi(options: {
  decisionStatus?: number
  loginStatus?: number
  requestCreateStatus?: number
  requestsStatus?: number
  requests?: MockRequest[]
  role?: 'Employee' | 'Manager' | 'Admin'
  workflow?: {
    requestId: number
    workflowName: string
    workflowVersion: number
    status: string
    startedAtUtc: string
    completedAtUtc: string | null
    stages: {
      id: number
      sequence: number
      status: 'Inactive' | 'Active' | 'Approved' | 'Rejected' | 'Cancelled'
      approverUserId: number
      approverName: string
      deadlineMinutes: number | null
      activatedAtUtc: string | null
      dueAtUtc: string | null
      decidedAtUtc: string | null
      decision: string | null
      comment: string | null
      escalatedAtUtc: string | null
    }[]
  }
  users?: { id: number; fullName: string; email: string; role: string; departmentId: number | null; createdAtUtc: string; isActive?: boolean }[]
  departments?: { id: number; name: string; code: string; isActive: boolean; createdAtUtc: string }[]
  workflowRules?: unknown[]
  notifications?: {
    id: number
    requestId: number | null
    type: string
    message: string
    createdAtUtc: string
    readAtUtc: string | null
  }[]
} = {}) {
  let requests: MockRequest[] = options.requests ?? [{ ...pendingRequest }]
  let availableRequestTypes = requestTypes.map((item) => ({ ...item }))
  const role = options.role ?? 'Manager'
  let users = options.users ?? []
  const departments = options.departments ?? []
  let workflowRules = options.workflowRules ?? []
  let notifications = options.notifications ?? []
  let profile = {
    userId: '42',
    fullName: 'Ali Manager',
    email: 'ali.manager@example.test',
    role,
    departmentId: null as number | null,
    departmentName: null as string | null,
    phoneNumber: null as string | null,
    hasProfileImage: false,
    profileImageUrl: '/api/profile/users/42/photo',
  }
  const calls: { method: string; path: string; body?: string }[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost')
    const method = init?.method ?? 'GET'
    calls.push({ method, path: url.pathname, ...(init?.body ? { body: String(init.body) } : {}) })

    if (url.pathname === '/api/auth/login') {
      if (options.loginStatus) {
        return jsonResponse(options.loginStatus, { message: 'Invalid email or password.' })
      }
      return jsonResponse(200, {
        id: 42,
        fullName: 'Ali Manager',
        email: 'ali.manager@example.test',
        role,
        accessToken: 'test-session',
        expiresAtUtc: '2026-10-20T20:00:00Z',
      })
    }
    if (url.pathname === '/api/auth/me') {
      return jsonResponse(200, profile)
    }
    if (url.pathname === '/api/analytics/dashboard') {
      const today = new Date().toISOString().slice(0, 10)
      const analytics = {
        role,
        from: url.searchParams.get('from') ?? today,
        to: url.searchParams.get('to') ?? today,
        totalRequests: requests.length,
        statusDistribution: [
          { label: 'Pending', count: requests.filter((item) => item.status === 'Pending').length },
          { label: 'Approved', count: 0 },
          { label: 'Rejected', count: 0 },
          { label: 'Cancelled', count: 0 },
        ],
        submissionTrend: [{ date: today, count: requests.length }],
        departmentPerformance: [],
        approvalPerformance: [],
        requestTypes: requestTypes.map((item) => ({
          code: item.code,
          name: item.displayName,
          count: 0,
        })),
        assignedRequestTrend: [{ date: today, count: requests.length }],
        managerRequestStatus: [
          { label: 'Pending', count: requests.length },
          { label: 'Completed', count: 0 },
        ],
        decisionActivity: [{ date: today, count: 0 }],
        averageDecisionTimeHours: null,
        priorityDistribution: [{ label: 'Unspecified', count: 0 }],
        approvalProgress: [
          { label: 'Pending', count: requests.length },
          { label: 'Approved', count: 0 },
          { label: 'Rejected', count: 0 },
          { label: 'Cancelled', count: 0 },
          { label: 'Awaiting routing', count: 0 },
        ],
        adminSummary: role === 'Admin' ? {
          totalRequests: requests.length,
          pendingRequests: requests.filter((item) => item.status === 'Pending').length,
          approvedRequests: 0,
          rejectedRequests: 0,
          cancelledRequests: 0,
          approvalRatePercent: null,
          averageApprovalTimeHours: null,
          overdueRequests: 0,
          activeEmployees: 3,
          activeManagers: 1,
        } : null,
        managerSummary: role === 'Manager' ? {
          assignedRequests: requests.length,
          pendingApprovals: requests.length,
          approvedRequests: 0,
          rejectedRequests: 0,
          overdueApprovals: 0,
          averageDecisionTimeHours: null,
        } : null,
        employeeSummary: role === 'Employee' ? {
          totalRequests: requests.length,
          pendingRequests: requests.filter((item) => item.status === 'Pending').length,
          approvedRequests: 0,
          rejectedRequests: 0,
          cancelledRequests: 0,
        } : null,
        workflowBottlenecks: [],
      }
      return jsonResponse(200, analytics)
    }
    if (url.pathname === '/api/reports/filter-options') {
      return jsonResponse(200, {
        departments: [{ id: 7, name: 'Operations' }],
        employees: [{ id: 43, name: 'Taylor Employee' }],
        managers: role === 'Employee' ? [] : [{ id: 42, name: 'Ali Manager' }],
        requestTypes: requestTypes.map((item) => ({ id: item.id, name: item.displayName })),
      })
    }
    if (url.pathname.startsWith('/api/reports/')) {
      return jsonResponse(200, {
        items: [],
        totalCount: 0,
        page: Number(url.searchParams.get('page') ?? 1),
        pageSize: Number(url.searchParams.get('pageSize') ?? 20),
      })
    }
    if (url.pathname === '/api/profile/me' && method === 'PATCH') {
      const body = JSON.parse(String(init?.body ?? '{}')) as {
        fullName: string
        phoneNumber: string
      }
      profile = { ...profile, ...body }
      return jsonResponse(200, profile)
    }
    if (url.pathname === '/api/profile/me/photo' && method === 'PUT') {
      profile = { ...profile, hasProfileImage: true }
      return jsonResponse(200, profile)
    }
    if (url.pathname === '/api/profile/me/photo' && method === 'DELETE') {
      profile = { ...profile, hasProfileImage: false }
      return new Response(null, { status: 204 })
    }
    if (url.pathname === '/api/notifications' && method === 'GET') {
      const page = Number(url.searchParams.get('page') ?? 1)
      const pageSize = Number(url.searchParams.get('pageSize') ?? 20)
      return jsonResponse(200, {
        items: notifications.slice((page - 1) * pageSize, page * pageSize),
        totalCount: notifications.length,
        unreadCount: notifications.filter((notification) => notification.readAtUtc == null).length,
        page,
        pageSize,
      })
    }
    if (/^\/api\/notifications\/\d+\/read$/.test(url.pathname) && method === 'PATCH') {
      return new Response(null, { status: 204 })
    }
    if (url.pathname === '/api/requests' && method === 'GET') {
      if (options.requestsStatus && options.requestsStatus >= 400) {
        return jsonResponse(options.requestsStatus, { message: 'Requests service unavailable.' })
      }
      return jsonResponse(200, requests)
    }
    if (url.pathname === '/api/requests' && method === 'POST') {
      if (options.requestCreateStatus && options.requestCreateStatus >= 400) {
        return jsonResponse(options.requestCreateStatus, { message: 'Request could not be submitted.' })
      }
      const body = JSON.parse(String(init?.body ?? '{}')) as {
        requestTypeCode: string
        amount?: number
        priority: 'Low' | 'Medium' | 'High' | 'Urgent'
        description?: string
        materialPurchase?: Record<string, unknown>
        leave?: Record<string, unknown>
        expenseReimbursement?: Record<string, unknown>
        itAccess?: Record<string, unknown>
        travel?: Record<string, unknown>
        workFromHome?: Record<string, unknown>
      }
      const requestTypeCode = String(body.requestTypeCode ?? '')
      const type = availableRequestTypes.find((item) => item.code === requestTypeCode)
      const details = (body.materialPurchase ?? body.leave
        ?? body.expenseReimbursement ?? body.itAccess
        ?? body.travel ?? body.workFromHome ?? {}) as Record<string, unknown>
      const title = requestTypeCode === 'MATERIAL_PURCHASE'
        ? String(details.itemName)
        : requestTypeCode === 'LEAVE'
          ? `${String(details.leaveType)} leave request`
          : requestTypeCode === 'EXPENSE_REIMBURSEMENT'
            ? `${String(details.category)} reimbursement`
            : requestTypeCode === 'IT_ACCESS'
              ? `${String(details.systemApplication)} access request`
              : requestTypeCode === 'TRAVEL'
                ? `${String(details.destination)} travel request`
                : 'Work from home request'
      const createdRequest = {
        ...pendingRequest,
        id: 502+ requests.length,
        title,
        description: typeof body.description === 'string' ? body.description : null,
        amount: typeof body.amount === 'number' ? body.amount : null,
        requestTypeId: type?.id ?? null,
        requestTypeCode,
        requestTypeName: type?.displayName ?? null,
        requestDetails: details,
        priority: body.priority,
        status: 'Pending',
        submittedBy: 'Ali Manager',
        assignments: [],
      }
      requests = [...requests, createdRequest]
      return jsonResponse(201, createdRequest)
    }
    const cancellationRequestMatch = url.pathname.match(/^\/api\/requests\/(\d+)\/cancellation-requests$/)
    if (cancellationRequestMatch && method === 'POST') {
      const originalRequestId = Number(cancellationRequestMatch[1])
      const originalRequest = requests.find((item) => item.id === originalRequestId)
      if (originalRequest) {
        const body = JSON.parse(String(init?.body ?? '{}')) as { reason: string }
        const cancellationRequest = {
          ...originalRequest,
          id: 600 + requests.length,
          title: `Cancellation for REQ-${String(originalRequestId).padStart(4, '0')}`,
          status: 'Pending',
          requestDetails: {
            ...originalRequest.requestDetails,
            cancellationOfRequestId: originalRequestId,
            reason: body.reason,
          },
        }
        requests = [...requests, cancellationRequest]
        return jsonResponse(201, cancellationRequest)
      }
    }
    if (url.pathname.match(/^\/api\/requests\/\d+\/workflow$/) && method === 'GET') {
      return options.workflow
        ? jsonResponse(200, options.workflow)
        : jsonResponse(404, { message: 'Workflow not found.' })
    }
    if (url.pathname === '/api/admin/users' && method === 'GET') {
      return jsonResponse(200, users)
    }
    const userRoleMatch = url.pathname.match(/^\/api\/admin\/users\/(\d+)\/role$/)
    if (userRoleMatch && method === 'PATCH') {
      const id = Number(userRoleMatch[1])
      const body = JSON.parse(String(init?.body ?? '{}')) as { role: string }
      users = users.map((user) => user.id === id ? { ...user, role: body.role } : user)
      const updated = users.find((user) => user.id === id)
      return updated ? jsonResponse(200, updated) : jsonResponse(404, { message: 'User not found.' })
    }
    const userDepartmentMatch = url.pathname.match(/^\/api\/admin\/users\/(\d+)\/department$/)
    if (userDepartmentMatch && method === 'PATCH') {
      const id = Number(userDepartmentMatch[1])
      const body = JSON.parse(String(init?.body ?? '{}')) as { departmentId: number | null }
      users = users.map((user) => user.id === id ? { ...user, departmentId: body.departmentId } : user)
      const updated = users.find((user) => user.id === id)
      return updated ? jsonResponse(200, updated) : jsonResponse(404, { message: 'User not found.' })
    }
    if (url.pathname === '/api/admin/departments' && method === 'GET') {
      return jsonResponse(200, departments)
    }
    if (url.pathname === '/api/request-types' && method === 'GET') {
      return jsonResponse(200, availableRequestTypes.filter((item) => item.isEnabled))
    }
    if (url.pathname === '/api/admin/request-types' && method === 'GET') {
      return jsonResponse(200, availableRequestTypes)
    }
    if (/^\/api\/admin\/request-types\/\d+\/enabled$/.test(url.pathname) && method === 'PATCH') {
      const id = Number(url.pathname.match(/request-types\/(\d+)/)?.[1])
      const body = JSON.parse(String(init?.body ?? '{}')) as { isEnabled: boolean }
      const updated = availableRequestTypes.find((item) => item.id === id)
      if (updated) {
        updated.isEnabled = body.isEnabled
        return jsonResponse(200, updated)
      }
    }
    if (url.pathname === '/api/admin/workflows' && method === 'GET') {
      return jsonResponse(200, workflowRules)
    }
    if (url.pathname === '/api/admin/workflows' && method === 'POST') {
      const body = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>
      const rule = {
        id: 77,
        ...body,
        version: 1,
        departmentName: null,
        status: 'Draft',
        createdAtUtc: '2026-10-20T12:00:00Z',
        publishedAtUtc: null,
      }
      workflowRules = [rule]
      return jsonResponse(201, rule)
    }
    if (url.pathname.match(/^\/api\/admin\/workflows\/\d+$/) && method === 'PUT') {
      const body = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>
      const existing = workflowRules[0] as Record<string, unknown> | undefined
      if (existing) {
        const rule = { ...existing, ...body }
        workflowRules = [rule]
        return jsonResponse(200, rule)
      }
    }
    if (url.pathname.match(/^\/api\/requests\/\d+\/assignments$/) && method === 'POST') {
      const requestId = Number(url.pathname.match(/\/requests\/(\d+)/)?.[1])
      const request = requests.find((item) => item.id === requestId)
      const body = JSON.parse(String(init?.body ?? '{}')) as { approverUserId: number }
      const manager = users.find((user) => user.id === body.approverUserId)
      if (request && manager) {
        const assignment = {
          id: 902,
          approverName: manager.fullName,
          approverEmail: manager.email,
          status: 'Pending',
          assignedAtUtc: '2026-10-19T14:00:00Z',
          decidedAtUtc: null,
        }
        requests = requests.map((item) => item.id === requestId
          ? { ...request, assignments: [...(request.assignments ?? []), assignment] }
          : item)
        return jsonResponse(201, { message: 'Approval assignment created.' })
      }
    }
    if (/^\/api\/requests\/\d+\/attachments$/.test(url.pathname) && method === 'GET') {
      return jsonResponse(200, [])
    }
    const requestId = Number(url.pathname.match(/\/requests\/(\d+)/)?.[1])
    const request = requests.find((item) => item.id === requestId)
    if (url.pathname === `/api/requests/${requestId}` && method === 'GET' && request) {
      return jsonResponse(200, request)
    }
    if (url.pathname === `/api/requests/${requestId}/approve` && method === 'PATCH' && request) {
      if (options.decisionStatus && options.decisionStatus >= 400) {
        return jsonResponse(options.decisionStatus, { message: 'Request is no longer pending.' })
      }
      requests = requests.map((item) => item.id === request.id ? {
        ...request,
        status: 'Approved',
        assignments: request.assignments.map((assignment) => ({ ...assignment, status: 'Approved' })),
      } : item)
      return jsonResponse(200, { message: 'Request approved successfully.' })
    }
    if (url.pathname === `/api/requests/${requestId}/reject` && method === 'PATCH' && request) {
      requests = requests.map((item) => item.id === request.id ? {
        ...request,
        status: 'Rejected',
        assignments: request.assignments.map((assignment) => ({ ...assignment, status: 'Rejected' })),
      } : item)
      return jsonResponse(200, { message: 'Request rejected successfully.' })
    }
    return jsonResponse(404, { message: 'Not found.' })
  })
  vi.stubGlobal('fetch', fetchMock)
  return {
    calls,
    setRequestStatus(status: string) {
      requests = requests.map((request) => ({ ...request, status }))
    },
    setNotifications(value: typeof notifications) {
      notifications = value
    },
  }
}

async function signInAsManager(user = userEvent.setup()) {
  await user.type(screen.getByLabelText('Email address'), 'ali.manager@example.test')
  await user.type(screen.getByLabelText('Password'), 'local-test-password')
  await user.click(screen.getByRole('button', { name: /Sign in/ }))
  await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
  return user
}

async function signInAsAdmin(user = userEvent.setup()) {
  await user.type(screen.getByLabelText('Email address'), 'admin@example.test')
  await user.type(screen.getByLabelText('Password'), 'local-test-password')
  await user.click(screen.getByRole('button', { name: /Sign in/ }))
  await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
  return user
}

async function fillMaterialPurchaseRequest(
  user: ReturnType<typeof userEvent.setup>,
  itemName: string,
) {
  await user.type(screen.getByLabelText(/Item name/), itemName)
  await user.type(screen.getByLabelText(/Quantity/), '1')
  await user.type(screen.getByLabelText(/Estimated amount/), '25')
  await user.type(screen.getByLabelText(/Business justification/), 'Required for testing.')
}

describe('Manager dashboard', () => {
  beforeEach(() => {
    sessionStorage.clear()
    window.history.replaceState(null, '', '/')
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    setViewportWidth(1024)
    signalrMock.handlers = {}
  })

  it('filters the overview recent requests by status and search text', async () => {
    mockApi({
      role: 'Manager',
      requests: [
        { ...pendingRequest, department: 'Operations' },
        {
          ...pendingRequest,
          id: 318,
          title: 'Travel request',
          status: 'Approved',
          department: 'Finance',
          createdAtUtc: '2026-10-18T12:00:00Z',
        },
      ],
    })
    render(<App />)
    const user = await signInAsManager()
    expect(screen.getByRole('heading', { name: 'Recently assigned' })).toBeTruthy()
    expect(screen.getAllByText('Laptop replacement').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Travel request').length).toBeGreaterThan(0)
    expect(screen.getAllByText('REQ-0317').length).toBeGreaterThan(0)
    expect(document.querySelector('.dashboard-request-cell .dashboard-request-type')).toBeTruthy()

    const statusFilters = screen.getByRole('group', { name: 'Filter recent requests by status' })
    await user.click(within(statusFilters).getByRole('button', { name: /Pending/ }))
    expect(screen.getAllByText('Laptop replacement').length).toBeGreaterThan(0)
    expect(screen.queryAllByText('Travel request')).toHaveLength(0)

    await user.click(within(statusFilters).getByRole('button', { name: /All requests/ }))
    await user.type(screen.getByRole('textbox', { name: 'Search recent requests' }), 'Finance')
    expect(screen.getAllByText('Travel request').length).toBeGreaterThan(0)
    expect(screen.queryAllByText('Laptop replacement')).toHaveLength(0)
  })

  it('opens an accessible mobile navigation drawer and restores focus when dismissed', async () => {
    setViewportWidth(375)
    mockApi()
    render(<App />)
    const user = await signInAsManager()
    const menuButton = screen.getByRole('button', { name: 'Open navigation' })

    await user.click(menuButton)
    const drawer = screen.getByRole('dialog', { name: 'Navigation menu' })
    const homeLink = within(drawer).getByRole('link', { name: 'FlowDesk home' })
    await waitFor(() => expect(document.activeElement).toBe(homeLink))
    expect(menuButton.getAttribute('aria-expanded')).toBe('true')
    expect(document.body.style.overflow).toBe('hidden')

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Navigation menu' })).toBeNull())
    expect(document.activeElement).toBe(menuButton)
    expect(document.body.style.overflow).toBe('')
  }, 30000)

  it('traps keyboard focus in the mobile drawer and closes it after navigation', async () => {
    setViewportWidth(375)
    mockApi()
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: 'Open navigation' }))

    const drawer = screen.getByRole('dialog', { name: 'Navigation menu' })
    const homeLink = within(drawer).getByRole('link', { name: 'FlowDesk home' })
    const profileButton = within(drawer).getByRole('button', { name: 'Open profile for Ali Manager' })
    await waitFor(() => expect(document.activeElement).toBe(homeLink))

    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(profileButton)
    fireEvent.keyDown(document, { key: 'Tab' })
    expect(document.activeElement).toBe(homeLink)

    await user.click(within(drawer).getByRole('button', { name: 'Request History' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Navigation menu' })).toBeNull())
    expect(window.location.hash).toBe('#/request-history')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Open navigation' }))

    await user.click(screen.getByRole('button', { name: 'Open navigation' }))
    const reopenedDrawer = screen.getByRole('dialog', { name: 'Navigation menu' })
    await user.click(within(reopenedDrawer).getByRole('button', { name: 'Dashboard' }))
    expect(await screen.findByRole('heading', {
      name: /Good (morning|afternoon|evening), Ali/,
    })).toBeTruthy()
  })

  it('signs in, resolves the Manager role, and shows only the assigned request with review details', async () => {
    mockApi()
    render(<App />)
    const user = await signInAsManager()

    expect(screen.getByRole('button', { name: 'Assigned to Me' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Request History' })).toBeTruthy()
    expect(screen.getByText('Pending Approvals')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Assigned to Me' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Taylor Employee')).toBeTruthy()
    expect(within(dialog).getByText('Operations')).toBeTruthy()
    expect(within(dialog).getByText('Required for the design team.')).toBeTruthy()
    expect(within(dialog).getByText(/Assigned to Ali Manager/)).toBeTruthy()
    expect(within(dialog).getByText(/legacy manual-assignment workflow/)).toBeTruthy()
  })

  it('shows the correct date and charged units for a half-day leave request', async () => {
    mockApi({
      requests: [{
        ...pendingRequest,
        title: 'Annual leave request',
        description: 'QA half-day leave test',
        amount: null,
        requestTypeCode: 'LEAVE',
        requestTypeName: 'Leave Request',
        requestDetails: {
          leaveType: 'Annual',
          startDate: '2026-11-02',
          endDate: '2026-11-02',
          reason: 'QA half-day leave test',
          totalLeaveDays: 1,
          calculatedLeaveUnits: 0.5,
        },
      }],
    })
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: 'Assigned to Me' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getAllByText('Nov 2, 2026')).toHaveLength(2)
    expect(within(dialog).getByText('0.5')).toBeTruthy()
  })

  it('loads Manager-only analytics and offers the supported date ranges', async () => {
    const { calls } = mockApi()
    render(<App />)
    const user = await signInAsManager()

    expect(screen.getByRole('region', { name: 'Dashboard analytics' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Summary statistics' }).children).toHaveLength(4)
    expect(screen.queryByLabelText('Manager analytics summary')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Assigned request trend' })).toBeNull()
    expect(screen.queryByRole('region', { name: 'Quick actions' })).toBeNull()
    expect(screen.getByRole('button', { name: 'View all analytics' })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'View all analytics' }))
    expect(await screen.findByRole('region', { name: 'Assigned request trend' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Manager decision activity' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Assigned request priority distribution' })).toBeTruthy()
    const managerSummary = screen.getByLabelText('Manager analytics summary')
    expect(managerSummary).toBeTruthy()
    expect(within(managerSummary).getByText('Average decision time')).toBeTruthy()
    for (const range of ['Last 7 days', 'Last 30 days', 'Last 90 days', 'Custom']) {
      expect(screen.getByRole('button', { name: range })).toBeTruthy()
    }
    const analyticsRequests = calls.filter((call) => call.path === '/api/analytics/dashboard').length
    await user.click(screen.getByRole('button', { name: 'Last 7 days' }))
    await waitFor(() => {
      expect(calls.filter((call) => call.path === '/api/analytics/dashboard').length).toBe(analyticsRequests + 1)
    })
    await user.click(screen.getByRole('button', { name: 'Custom' }))
    expect(screen.getByLabelText('Analytics start date')).toBeTruthy()
    expect(screen.getByLabelText('Analytics end date')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Reset filters' }))
    await waitFor(() => {
      expect(calls.filter((call) => call.path === '/api/analytics/dashboard').length).toBe(analyticsRequests + 2)
    })
    expect(window.location.hash).toBe('#/analytics')
    await act(async () => { window.history.back() })
    expect(await screen.findByRole('region', { name: 'Dashboard analytics' })).toBeTruthy()
    await act(async () => { window.history.forward() })
    expect(await screen.findByRole('region', { name: 'Assigned request trend' })).toBeTruthy()
  })

  it('loads Admin workspace analytics including department and approval performance', async () => {
    mockApi({ role: 'Admin' })
    render(<App />)
    await signInAsAdmin()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Analytics' }))

    expect(await screen.findByRole('region', { name: 'Request status distribution donut chart' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Department request status comparison' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Average approval time by department' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Request type volume' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Request priority distribution' })).toBeTruthy()
    expect(screen.getByLabelText('Admin analytics summary')).toBeTruthy()
    expect(screen.getByText('Workflow bottlenecks')).toBeTruthy()
  })

  it('assigns stable colors to request types independent of category order', () => {
    const categories = [
      { code: 'EXPENSE_REIMBURSEMENT', name: 'Expense Reimbursement', color: '#0D9488' },
      { code: 'IT_ACCESS', name: 'IT Access', color: '#3B82F6' },
      { code: 'LEAVE_REQUEST', name: 'Leave Request', color: '#8B5CF6' },
      { code: 'MATERIAL_PURCHASE', name: 'Material Purchase', color: '#F59E0B' },
      { code: 'TRAVEL_REQUEST', name: 'Travel Request', color: '#EC4899' },
      { code: 'WORK_FROM_HOME', name: 'Work From Home', color: '#10B981' },
      { code: 'OTHER_LEGACY', name: 'Other / Legacy', color: '#64748B' },
    ]
    const getColors = (items: typeof categories) => Object.fromEntries(
      items.map(({ code, name }) => [code, requestTypeColor(code, name)]),
    )

    expect(getColors(categories)).toEqual(getColors([...categories].reverse()))
    for (const { code, name, color } of categories) {
      expect(requestTypeColor(code, name)).toBe(color)
    }
  })

  it('uses distinct colors for each approval progress status', () => {
    expect([
      approvalProgressColor('Pending'),
      approvalProgressColor('Approved'),
      approvalProgressColor('Rejected'),
      approvalProgressColor('Cancelled'),
      approvalProgressColor('Awaiting routing'),
    ]).toEqual(['#F59E0B', '#10B981', '#EF4444', '#64748B', '#3B82F6'])
  })

  it('uses distinct colors for request priorities', () => {
    expect([
      priorityColor('Low'),
      priorityColor('Medium'),
      priorityColor('High'),
      priorityColor('Urgent'),
      priorityColor('Unspecified'),
    ]).toEqual(['#10B981', '#0D9488', '#F59E0B', '#EF4444', '#64748B'])
  })

  it('filters requests using the single top status filter', async () => {
    mockApi({
      role: 'Admin',
      requests: [
        { ...pendingRequest },
        { ...pendingRequest, id: 318, title: 'Approved request', status: 'Approved' },
      ],
    })
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'All Requests' }))

    expect(screen.queryByText('Status Type')).toBeNull()
    const statusFilter = screen.getByLabelText('Filter requests by status')
    expect((statusFilter as HTMLSelectElement).value).toBe('All statuses')
    await user.selectOptions(statusFilter, 'Approved')

    expect(screen.getAllByText('Approved request').length).toBeGreaterThan(0)
    expect(screen.queryByText('Laptop replacement')).toBeNull()
  })

  it('shows concise assignment summaries and an accessible list of all assignment records', async () => {
    const longTitle = 'A very long request title that should remain limited to two visible lines in the request list'
    mockApi({
      role: 'Admin',
      requests: [
        { ...pendingRequest, id: 401, title: longTitle, assignments: [] },
        { ...pendingRequest, id: 402, assignments: [{ ...pendingRequest.assignments[0] }] },
        {
          ...pendingRequest,
          id: 403,
          assignments: [
            { ...pendingRequest.assignments[0], id: 903, approverName: 'Ali Ahmed', status: 'Approved' },
            { ...pendingRequest.assignments[0], id: 904, approverName: 'FlowDesk Development IT Manager 01', status: 'Pending' },
            { ...pendingRequest.assignments[0], id: 905, approverName: 'FlowDesk Development IT Manager 02', status: 'Approved' },
          ],
        },
      ],
    })
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'All Requests' }))

    expect(screen.getAllByText('Not assigned')).not.toHaveLength(0)
    expect(screen.getAllByText('Ali Manager').length).toBeGreaterThan(0)
    const moreButtons = screen.getAllByText('+2 more')
    expect(moreButtons.length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: '2 more assigned managers' }).length).toBeGreaterThan(0)
    await user.click(moreButtons[0])
    expect(screen.getAllByText('FlowDesk Development IT Manager 01').length).toBeGreaterThan(0)
    expect(screen.getAllByText('FlowDesk Development IT Manager 02').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Assignment records').length).toBeGreaterThan(0)
    expect(screen.getAllByText(/may include previous approvers/i).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Pending').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Approved').length).toBeGreaterThan(0)
    expect(document.querySelector('.request-title-copy > strong')?.getAttribute('title')).toBe(longTitle)
  })

  it('loads Employee-owned analytics without exposing Manager chart panels', async () => {
    mockApi({ role: 'Employee' })
    render(<App />)
    await signInAsManager()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Analytics' }))

    expect(await screen.findByRole('region', { name: 'My request status distribution' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'My approval progress summary' })).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Manager decision activity' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Analytics' }).className).toContain('active')
  })

  it('navigates to role-scoped reports and loads server-paged report tabs', async () => {
    const { calls } = mockApi({ role: 'Manager' })
    render(<App />)
    const user = await signInAsManager()

    expect(screen.getByRole('button', { name: 'Reports' })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Reports' }))
    expect(await screen.findByRole('region', { name: 'Reports' })).toBeTruthy()
    expect(screen.getByLabelText('Employee')).toBeTruthy()
    expect(screen.getByLabelText('Manager')).toBeTruthy()
    expect(screen.getByText('No report records found')).toBeTruthy()
    await waitFor(() => {
      expect(calls.some((call) => call.path === '/api/reports/filter-options')).toBe(true)
      expect(calls.some((call) => call.path === '/api/reports/requests')).toBe(true)
    })

    await user.click(screen.getByRole('tab', { name: 'Approval History' }))
    await waitFor(() => {
      expect(calls.some((call) => call.path === '/api/reports/approval-history')).toBe(true)
    })
    expect(screen.getByRole('button', { name: 'Next' }).hasAttribute('disabled')).toBe(true)
  })

  it('shows accessible empty states when the selected range contains no requests', async () => {
    mockApi({ requests: [] })
    render(<App />)
    await signInAsManager()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Analytics' }))

    expect(await screen.findAllByText('No data for this date range.')).not.toHaveLength(0)
    expect(screen.getByText('No completed decisions with timing data.')).toBeTruthy()
  })

  it('requires confirmation and displays the updated status after an approved response', async () => {
    const { calls } = mockApi()
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: 'Assigned to Me' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))
    await user.click(await screen.findByRole('button', { name: /Approve request/ }))

    expect(screen.getByText('Confirm approve?')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Confirm approve' }))

    await waitFor(() => {
      expect(calls.some((call) => call.method === 'PATCH' && call.path.endsWith('/approve'))).toBe(true)
    })
    expect(await screen.findByText('Request approved successfully.')).toBeTruthy()
    await user.click(within(screen.getByRole('navigation', { name: 'Main navigation' })).getByRole('button', { name: 'Request History' }))
    expect((await screen.findAllByText('Approved')).length).toBeGreaterThan(0)
  })

  it('shows workflow stage progress and requires a reason when rejecting a workflow request', async () => {
    const { calls } = mockApi({
      workflow: {
        requestId: 317,
        workflowName: 'Department approval',
        workflowVersion: 2,
        status: 'InProgress',
        startedAtUtc: '2026-10-19T12:00:00Z',
        completedAtUtc: null,
        stages: [{
          id: 1,
          sequence: 1,
          status: 'Active',
          approverUserId: 42,
          approverName: 'Ali Manager',
          deadlineMinutes: 60,
          activatedAtUtc: '2026-10-19T13:00:00Z',
          dueAtUtc: '2026-10-19T14:00:00Z',
          decidedAtUtc: null,
          decision: null,
          comment: null,
          escalatedAtUtc: null,
        }],
      },
    })
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: 'Assigned to Me' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Department approval')).toBeTruthy()
    expect(within(dialog).getByText('Stage 1 · Ali Manager')).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: 'Reject' }))
    await user.click(within(dialog).getByRole('button', { name: 'Confirm reject' }))
    expect(within(dialog).getByRole('alert').textContent).toContain('rejection reason')
    expect(calls.some((call) => call.method === 'PATCH' && call.path.endsWith('/reject'))).toBe(false)

    await user.type(within(dialog).getByLabelText(/Decision comment/), 'Missing purchase justification')
    await user.click(within(dialog).getByRole('button', { name: 'Confirm reject' }))
    await waitFor(() => expect(calls.some((call) => call.method === 'PATCH' && call.path.endsWith('/reject'))).toBe(true))
    expect(calls.find((call) => call.path.endsWith('/reject'))?.body).toBe(JSON.stringify({ comment: 'Missing purchase justification' }))
  })

  it('renders all three approval stages with approvers, decisions, comments, and final outcome', async () => {
    const stages = [
      { id: 1, sequence: 1, status: 'Approved' as const, approverUserId: 51, approverName: 'Mina Stage One', deadlineMinutes: 60, activatedAtUtc: '2026-10-19T13:00:00Z', dueAtUtc: null, decidedAtUtc: '2026-10-19T13:30:00Z', decision: 'Approved', comment: 'Budget confirmed.', escalatedAtUtc: null },
      { id: 2, sequence: 2, status: 'Approved' as const, approverUserId: 52, approverName: 'Noah Stage Two', deadlineMinutes: 60, activatedAtUtc: '2026-10-19T13:30:00Z', dueAtUtc: null, decidedAtUtc: '2026-10-19T14:00:00Z', decision: 'Approved', comment: 'Department approved.', escalatedAtUtc: null },
      { id: 3, sequence: 3, status: 'Approved' as const, approverUserId: 53, approverName: 'Sara Stage Three', deadlineMinutes: 60, activatedAtUtc: '2026-10-19T14:00:00Z', dueAtUtc: null, decidedAtUtc: '2026-10-19T14:30:00Z', decision: 'Approved', comment: 'Final review complete.', escalatedAtUtc: null },
    ]
    mockApi({
      requests: [{ ...pendingRequest, status: 'Approved', assignments: pendingRequest.assignments.map((assignment) => ({ ...assignment, status: 'Approved' })) }],
      workflow: {
        requestId: pendingRequest.id,
        workflowName: 'Purchase approval',
        workflowVersion: 1,
        status: 'Approved',
        startedAtUtc: '2026-10-19T12:00:00Z',
        completedAtUtc: '2026-10-19T14:30:00Z',
        stages,
      },
    })
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: 'Request History' }))
    await user.click(await screen.findByRole('button', { name: 'View' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Stage 1 · Mina Stage One')).toBeTruthy()
    expect(within(dialog).getByText('Stage 2 · Noah Stage Two')).toBeTruthy()
    expect(within(dialog).getByText('Stage 3 · Sara Stage Three')).toBeTruthy()
    expect(within(dialog).getByText('Budget confirmed.')).toBeTruthy()
    expect(within(dialog).getByText('Department approved.')).toBeTruthy()
    expect(within(dialog).getByText('Final review complete.')).toBeTruthy()
    expect(within(dialog).getByText('Final outcome')).toBeTruthy()
    expect(within(dialog).getAllByText('Approved').length).toBeGreaterThan(0)
  })

  it('does not show a success state when the approval endpoint rejects the action', async () => {
    mockApi({ decisionStatus: 409 })
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: 'Assigned to Me' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))
    await user.click(await screen.findByRole('button', { name: /Approve request/ }))
    await user.click(screen.getByRole('button', { name: 'Confirm approve' }))

    expect((await screen.findAllByText('Request is no longer pending.')).length).toBeGreaterThan(0)
    expect(screen.queryByText('Request approved successfully.')).toBeNull()
    expect(screen.getAllByText('Pending').length).toBeGreaterThan(0)
  })

  it('keeps the login screen and displays the API error for invalid credentials', async () => {
    mockApi({ loginStatus: 401 })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'invalid@example.test')
    await user.type(screen.getByLabelText('Password'), 'incorrect-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))

    expect((await screen.findByRole('alert')).textContent).toContain('Invalid email or password.')
    expect(screen.getByRole('heading', { name: 'Welcome back' })).toBeTruthy()
  })

  it('paginates the Manager assigned request list', async () => {
    const requests = Array.from({ length: 9 }, (_, index) => ({
      ...pendingRequest,
      id: pendingRequest.id + index,
      title: `Request ${index + 1}`,
    }))
    mockApi({ requests })
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: 'Assigned to Me' }))

    expect(screen.getAllByText('Request 1').length).toBeGreaterThan(0)
    expect(screen.queryAllByText('Request 9')).toHaveLength(0)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getAllByText('Request 9').length).toBeGreaterThan(0)
    expect(screen.getByText('Page 2 of 2')).toBeTruthy()
  })

  it('shows Admin management navigation and assigns a pending request to an eligible same-department Manager', async () => {
    const { calls } = mockApi({
      role: 'Admin',
      requests: [
        { ...pendingRequest, assignments: [] },
        { ...pendingRequest, id: 318, title: 'Already approved request', status: 'Approved', assignments: [] },
      ],
      users: [{
        id: 88,
        fullName: 'Morgan Manager',
        email: 'morgan@example.test',
        role: 'Manager',
        departmentId: 7,
        createdAtUtc: '2026-10-01T12:00:00Z',
      }],
      departments: [{
        id: 7,
        name: 'Operations',
        code: 'OPS',
        isActive: true,
        createdAtUtc: '2026-01-01T00:00:00Z',
      }],
    })
    render(<App />)
    const user = await signInAsAdmin()
    expect(screen.getByRole('button', { name: 'User Management' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Departments' })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Assignment Management' }))
    expect(screen.queryByText('Status Type')).toBeNull()
    expect(screen.getByLabelText('Filter requests by status')).toBeTruthy()
    await user.selectOptions(screen.getByLabelText('Filter requests by status'), 'Pending')
    const assignmentTable = screen.getByRole('table')
    expect(assignmentTable.classList.contains('assignment-management-table')).toBe(true)
    expect(within(assignmentTable).queryByText('Already approved request')).toBeNull()
    expect(screen.getByText(/Assign pending requests to an eligible Manager/)).toBeTruthy()
    await user.click(await within(assignmentTable).findByRole('button', { name: 'Assign Manager' }))

    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Manager'), '88')
    await user.click(within(dialog).getByRole('button', { name: 'Assign Manager' }))

    await waitFor(() => {
      expect(screen.getByText('Manager assigned successfully.')).toBeTruthy()
    })
    expect(calls.some((call) =>
      call.path === '/api/requests/317/assignments' &&
      call.method === 'POST' &&
      call.body === JSON.stringify({ approverUserId: 88 }),
    )).toBe(true)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getAllByText('Assigned').length).toBeGreaterThan(0)
  })

  it('keeps role and department read-only in the user table and updates them from the row dialog', async () => {
    const { calls } = mockApi({
      role: 'Admin',
      requests: [],
      users: [{
        id: 88,
        fullName: 'Morgan Employee',
        email: 'morgan@example.test',
        role: 'Employee',
        departmentId: 7,
        createdAtUtc: '2026-10-01T12:00:00Z',
        isActive: true,
      }],
      departments: [
        { id: 7, name: 'Human Resources', code: 'HR', isActive: true, createdAtUtc: '2026-01-01T00:00:00Z' },
        { id: 8, name: 'Operations', code: 'OPS', isActive: true, createdAtUtc: '2026-01-01T00:00:00Z' },
      ],
    })
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'User Management' }))

    const table = screen.getByRole('table')
    expect(within(table).queryByRole('combobox')).toBeNull()
    expect(within(table).getByText('Employee')).toBeTruthy()
    expect(within(table).getByText('Human Resources')).toBeTruthy()
    expect(within(table).getByRole('button', { name: 'View' })).toBeTruthy()
    expect(within(table).getByRole('button', { name: 'Deactivate' })).toBeTruthy()

    await user.click(within(table).getByRole('button', { name: 'Update' }))
    const dialog = await screen.findByRole('dialog', { name: 'Update user' })
    await user.selectOptions(within(dialog).getByLabelText('Role'), 'Manager')
    await user.selectOptions(within(dialog).getByLabelText('Department'), '8')
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }))

    await waitFor(() => {
      expect(within(screen.getByRole('table')).getByText('Manager')).toBeTruthy()
      expect(within(screen.getByRole('table')).getByText('Operations')).toBeTruthy()
    })
    expect(calls.some((call) =>
      call.path === '/api/admin/users/88/role' &&
      call.method === 'PATCH' &&
      call.body === JSON.stringify({ role: 'Manager' }),
    )).toBe(true)
    expect(calls.some((call) =>
      call.path === '/api/admin/users/88/department' &&
      call.method === 'PATCH' &&
      call.body === JSON.stringify({ departmentId: 8 }),
    )).toBe(true)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('shows only Employee navigation and the real request summary for an Employee', async () => {
    mockApi({ role: 'Employee' })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })

    expect(screen.getByText('Total Requests')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'My Requests' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'New Request' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'User Management' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Assigned to Me' })).toBeNull()
  })

  it('supports password visibility and the split login layout without exposing demo credentials', async () => {
    mockApi({ loginStatus: 401 })
    render(<App />)
    const password = screen.getByLabelText('Password') as HTMLInputElement
    const user = userEvent.setup()
    await user.type(password, 'private-test-password')
    expect(password.type).toBe('password')
    await user.click(screen.getByRole('button', { name: 'Show password' }))
    expect(password.type).toBe('text')
    expect(screen.getByRole('heading', { name: /Move every decision/ })).toBeTruthy()
    expect(screen.queryByText(/demo password/i)).toBeNull()
  })

  it('opens the Admin add-user and add-department modals with responsive form fields', async () => {
    mockApi({ role: 'Admin' })
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'User Management' }))
    await user.click(screen.getByRole('button', { name: /Add User/ }))

    let dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText('Full name')).toBeTruthy()
    expect(within(dialog).getByLabelText('Email')).toBeTruthy()
    expect(within(dialog).getByLabelText('Role')).toBeTruthy()
    expect(within(dialog).getByLabelText('Department')).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: 'Close' }))

    await user.click(screen.getByRole('button', { name: 'Departments' }))
    await user.click(screen.getByRole('button', { name: /Add department/ }))
    dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText(/Department name/)).toBeTruthy()
    expect(within(dialog).getByLabelText(/Department code/)).toBeTruthy()
  })

  it('creates an Admin workflow draft through the implemented API', async () => {
    const { calls } = mockApi({ role: 'Admin', workflowRules: [] })
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'Workflow Management' }))
    await screen.findByRole('heading', { name: 'Approval workflows' })
    expect(screen.getByText('No rules configured yet.')).toBeTruthy()

    await user.type(screen.getByLabelText('Workflow key'), 'department-approval')
    await user.type(screen.getByLabelText('Display name'), 'Department approval')
    await user.click(screen.getByRole('button', { name: 'Create draft' }))
    expect(await screen.findByText('Draft version 1 saved.')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Add stage' }))
    await user.click(screen.getByRole('button', { name: 'Save draft' }))

    expect(await screen.findByText('Draft version 1 saved.')).toBeTruthy()
    const updateCall = calls.find((call) => call.method === 'PUT' && call.path === '/api/admin/workflows/77')
    expect(updateCall).toBeTruthy()
    const body = JSON.parse(updateCall?.body ?? '{}') as {
      stages: { sequence: number }[]
    }
    expect(body.stages.map((stage) => stage.sequence)).toEqual([1, 2])
    expect(calls.some((call) => call.method === 'POST' && call.path === '/api/admin/workflows')).toBe(true)
  })

  it('routes workflow drafts by request type and hides amount thresholds for nonfinancial types', async () => {
    const { calls } = mockApi({ role: 'Admin', workflowRules: [] })
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'Workflow Management' }))
    await screen.findByRole('heading', { name: 'Approval workflows' })
    await user.type(screen.getByLabelText('Workflow key'), 'leave-approval')
    await user.type(screen.getByLabelText('Display name'), 'Leave approval')
    await user.selectOptions(screen.getByLabelText('Request type'), '2')
    expect(screen.queryByLabelText('Minimum amount')).toBeNull()
    expect(screen.queryByLabelText('Maximum amount (exclusive)')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Create draft' }))

    await waitFor(() => expect(calls.some(
      (call) => call.method === 'POST' && call.path === '/api/admin/workflows',
    )).toBe(true))
    const createCall = calls.find(
      (call) => call.method === 'POST' && call.path === '/api/admin/workflows',
    )
    const body = JSON.parse(createCall?.body ?? '{}') as {
      requestTypeId: number
      minimumAmount: number
      maximumAmountExclusive: number | null
    }
    expect(body.requestTypeId).toBe(2)
    expect(body.minimumAmount).toBe(0)
    expect(body.maximumAmountExclusive).toBeNull()
  })

  it('allows Admins to disable a request type without changing request records', async () => {
    const { calls } = mockApi({ role: 'Admin' })
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'Request Types' }))
    await screen.findByRole('heading', { name: 'Available request types' })
    await user.click(screen.getAllByRole('button', { name: 'Disable' })[0])

    await waitFor(() => expect(calls.some(
      (call) => call.method === 'PATCH' &&
        call.path === '/api/admin/request-types/1/enabled',
    )).toBe(true))
    expect(screen.getByText('Disabled')).toBeTruthy()
    expect(calls.some((call) => call.path === '/api/requests' && call.method === 'DELETE')).toBe(false)
  })

  it('does not present a failed initial request load as a successful empty state', async () => {
    mockApi({ requestsStatus: 503 })
    render(<App />)
    await signInAsManager()

    expect(await screen.findByText('Requests unavailable')).toBeTruthy()
    expect(screen.getByText('Requests service unavailable.')).toBeTruthy()
    expect(screen.queryByText('No assigned requests')).toBeNull()
  })

  it('submits an Employee request and reports the successful API response', async () => {
    const { calls } = mockApi({ role: 'Employee', requests: [] })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'New Request' }))

    await user.type(screen.getByLabelText(/Item name/), 'Laptop replacement')
    await user.type(screen.getByLabelText(/Quantity/), '1')
    await user.type(screen.getByLabelText(/Additional description/), 'Required for the design team.')
    await user.type(screen.getByLabelText(/Estimated amount/), '1250')
    await user.type(screen.getByLabelText(/Business justification/), 'Replace aging equipment.')
    await user.click(screen.getByRole('button', { name: /Submit request/ }))

    expect(await screen.findByText('Your request has been submitted.')).toBeTruthy()
    expect(screen.getAllByText('Laptop replacement').length).toBeGreaterThan(0)
    const createCall = calls.find((call) => call.method === 'POST' && call.path === '/api/requests')
    expect(createCall?.body).toBe(JSON.stringify({
      requestTypeCode: 'MATERIAL_PURCHASE',
      amount: 1250,
      priority: 'Medium',
      materialPurchase: {
        itemName: 'Laptop replacement',
        quantity: 1,
        businessJustification: 'Replace aging equipment.',
      },
      description: 'Required for the design team.',
    }))
    await user.click(await screen.findByRole('button', { name: 'View' }))
    expect(await screen.findByText('Required for the design team.')).toBeTruthy()
  })

  it('shows each request type with only its matching fields and amount rules', async () => {
    mockApi({ role: 'Employee', requests: [] })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'New Request' }))

    const typeSelect = screen.getByLabelText('Request type')
    expect(screen.getByLabelText(/Item name/)).toBeTruthy()
    expect(screen.getByLabelText(/Estimated amount/)).toBeTruthy()
    expect(screen.queryByLabelText(/Leave type/)).toBeNull()

    await user.selectOptions(typeSelect, 'LEAVE')
    expect(screen.getByLabelText(/Leave type/)).toBeTruthy()
    expect(screen.getByLabelText(/Start date/)).toBeTruthy()
    expect(screen.queryByLabelText(/Estimated amount/)).toBeNull()

    await user.selectOptions(typeSelect, 'EXPENSE_REIMBURSEMENT')
    expect(screen.getByLabelText(/Expense category/)).toBeTruthy()
    expect(screen.getByLabelText(/Expense date/)).toBeTruthy()
    expect(screen.getByLabelText(/^Amount/)).toBeTruthy()

    await user.selectOptions(typeSelect, 'IT_ACCESS')
    expect(screen.getByLabelText(/System or application/)).toBeTruthy()
    expect(screen.getByLabelText(/Requested access level/)).toBeTruthy()
    expect(screen.queryByLabelText(/^Amount/)).toBeNull()

    await user.selectOptions(typeSelect, 'TRAVEL')
    expect(screen.getByLabelText(/Destination/)).toBeTruthy()
    expect(screen.getByLabelText(/Start date/)).toBeTruthy()
    expect(screen.getByLabelText(/Estimated budget/)).toBeTruthy()

    await user.selectOptions(typeSelect, 'WORK_FROM_HOME')
    expect(screen.getByLabelText(/Start date/)).toBeTruthy()
    expect(screen.getByLabelText(/End date/)).toBeTruthy()
    expect(screen.queryByLabelText(/^Amount/)).toBeNull()
  })

  it('keeps Employee profile details read-only and allows secure photo changes', async () => {
    const { calls } = mockApi({ role: 'Employee', requests: [] })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'Profile' }))

    expect(screen.getByText('Full name')).toBeTruthy()
    expect(screen.getAllByText('Ali Manager').length).toBeGreaterThan(0)
    expect(screen.getByText('ali.manager@example.test')).toBeTruthy()
    expect(screen.getByText('Not provided')).toBeTruthy()
    expect(screen.queryByRole('textbox', { name: 'Full name' })).toBeNull()
    expect(screen.queryByRole('textbox', { name: 'Phone number' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Save profile' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull()

    const photo = new File(['profile image'], 'profile.png', { type: 'image/png' })
    await user.upload(screen.getByLabelText('Choose profile photo'), photo)
    await user.click(screen.getByRole('button', { name: 'Upload photo' }))
    expect(await screen.findByText('Profile photo updated.')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Remove photo' }))
    expect(await screen.findByText('Profile photo removed.')).toBeTruthy()
    expect(calls.some((call) => call.path === '/api/profile/me' && call.method === 'PATCH')).toBe(false)
  })

  it.each([
    ['RequestApproved', 'Approved'],
    ['RequestRejected', 'Rejected'],
    ['RequestCancelled', 'Cancelled'],
  ])('refreshes Employee list and open details after %s', async (notificationType, status) => {
    const { calls, setRequestStatus } = mockApi({ role: 'Employee' })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'My Requests' }))
    await user.click(await screen.findByRole('button', { name: 'View' }))
    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(signalrMock.handlers.notificationReceived).toBeDefined())

    const requestListCalls = () => calls.filter(
      (call) => call.method === 'GET' && call.path === '/api/requests',
    ).length
    expect(requestListCalls()).toBe(1)

    act(() => signalrMock.handlers.notificationReceived?.({
      id: 101,
      requestId: pendingRequest.id,
      type: 'StageAdvanced',
      message: 'Request advanced to another stage.',
      createdAtUtc: '2026-10-19T14:00:00Z',
      readAtUtc: null,
    }))
    await waitFor(() => expect(requestListCalls()).toBe(1))

    setRequestStatus(status)
    act(() => signalrMock.handlers.notificationReceived?.({
      id: 102,
      requestId: pendingRequest.id,
      type: notificationType,
      message: `Your request was ${status.toLowerCase()}.`,
      createdAtUtc: '2026-10-19T14:30:00Z',
      readAtUtc: null,
    }))
    await waitFor(() => expect(requestListCalls()).toBe(2))
    await waitFor(() => expect(within(dialog).getAllByText(status)).toHaveLength(2))
    expect(screen.getAllByText(status).length).toBeGreaterThan(2)
  })

  it('shows unread notifications, marks one read, and opens its related request', async () => {
    const { calls } = mockApi({
      role: 'Employee',
      notifications: [{
        id: 71,
        requestId: pendingRequest.id,
        type: 'RequestSubmitted',
        message: 'Your request was submitted.',
        createdAtUtc: '2026-10-19T12:00:00Z',
        readAtUtc: null,
      }],
    })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })

    await user.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }))
    const notification = await screen.findByRole('button', {
      name: /Your request was submitted/,
    })
    await user.click(notification)

    await waitFor(() => {
      expect(calls.some((call) => call.method === 'PATCH' && call.path === '/api/notifications/71/read')).toBe(true)
    })
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Laptop replacement')).toBeTruthy()
  })

  it('shows SignalR reconnecting status and refreshes the feed after reconnect', async () => {
    mockApi()
    render(<App />)
    const user = await signInAsManager()
    await user.click(screen.getByRole('button', { name: /^Notifications/ }))
    expect(await screen.findByText('connected')).toBeTruthy()

    act(() => signalrMock.handlers.reconnecting?.())
    expect(await screen.findByText('reconnecting')).toBeTruthy()
    act(() => signalrMock.handlers.reconnected?.())
    expect(await screen.findByText('connected')).toBeTruthy()
  })

  it('shows request-needs-assignment notifications to the Admin', async () => {
    mockApi({
      role: 'Admin',
      requests: [],
      notifications: [{
        id: 82,
        requestId: 317,
        type: 'RequestNeedsAdminAssignment',
        message: 'Request REQ-0317 requires manual Manager assignment.',
        createdAtUtc: '2026-10-19T12:00:00Z',
        readAtUtc: null,
      }],
    })
    render(<App />)
    const user = await signInAsAdmin()

    await user.click(await screen.findByRole('button', {
      name: 'Notifications, 1 unread',
    }))
    expect(await screen.findByText(
      'Request REQ-0317 requires manual Manager assignment.',
    )).toBeTruthy()
  })

  it.each([
    'RequestSubmitted',
    'RequestNeedsAdminAssignment',
  ])('shows live Admin %s notifications, persists them in the feed, and opens the request', async (type) => {
    const { setNotifications } = mockApi({ role: 'Admin' })
    render(<App />)
    const user = await signInAsAdmin()
    await waitFor(() => expect(signalrMock.handlers.notificationReceived).toBeDefined())

    const notification = {
      id: 91,
      requestId: pendingRequest.id,
      type,
      message: 'A new approval request "Laptop replacement" has been submitted and requires attention.',
      createdAtUtc: '2026-10-19T14:30:00Z',
      readAtUtc: null,
    }
    setNotifications([notification])
    act(() => signalrMock.handlers.notificationReceived?.(notification))
    await screen.findByText(
      `New Approval Request: ${notification.message}`,
    )
    expect(screen.getByRole('button', {
      name: 'Notifications, 1 unread',
    })).toBeTruthy()

    act(() => signalrMock.handlers.notificationReceived?.(notification))
    expect(screen.getAllByText(
      `New Approval Request: ${notification.message}`,
    )).toHaveLength(1)

    await user.click(screen.getByRole('button', {
      name: 'Notifications, 1 unread',
    }))
    await screen.findByRole('button', { name: /Laptop replacement/ })
    expect(screen.getByText('New Approval Request')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    await user.click(screen.getByRole('button', {
      name: 'Notifications, 1 unread',
    }))
    const notificationButton = await screen.findByRole('button', {
      name: /Laptop replacement/,
    })
    await user.click(notificationButton)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Laptop replacement')).toBeTruthy()
  })

  it('provides an accessible close control and keeps unsaved Admin forms open on Escape', async () => {
    mockApi({ role: 'Admin' })
    render(<App />)
    const user = await signInAsAdmin()
    await user.click(screen.getByRole('button', { name: 'User Management' }))
    await user.click(screen.getByRole('button', { name: /Add User/ }))

    const dialog = await screen.findByRole('dialog', { name: 'Add User' })
    expect(within(dialog).getByRole('button', { name: 'Close' })).toBeTruthy()
    await user.type(within(dialog).getByLabelText('Full name'), 'Unsaved User')
    act(() => {
      document.dispatchEvent(new KeyboardEvent('keydown', {
        key: 'Escape',
        bubbles: true,
      }))
    })
    expect(screen.getByRole('dialog', { name: 'Add User' })).toBeTruthy()

    const backdrop = dialog.parentElement
    if (!backdrop) throw new Error('Dialog backdrop was not rendered')
    fireEvent.mouseDown(backdrop, { target: backdrop })
    expect(screen.getByRole('dialog', { name: 'Add User' })).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog', { name: 'Add User' })).toBeNull()
  })

  it('keeps Admin dialogs usable at a narrow mobile viewport', async () => {
    const previousWidth = window.innerWidth
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      value: 360,
    })
    try {
      mockApi({ role: 'Admin' })
      render(<App />)
      const user = await signInAsAdmin()
      await user.click(screen.getByRole('button', { name: 'User Management' }))
      await user.click(screen.getByRole('button', { name: /Add User/ }))
      const dialog = await screen.findByRole('dialog', { name: 'Add User' })
      expect(dialog.classList.contains('detail-modal')).toBe(true)
      expect(within(dialog).getByLabelText('Full name')).toBeTruthy()
      expect(within(dialog).getByRole('button', { name: 'Close' })).toBeTruthy()
    } finally {
      Object.defineProperty(window, 'innerWidth', {
        configurable: true,
        value: previousWidth,
      })
    }
  })

  it('automatically dismisses success toasts after four seconds', async () => {
    const user = userEvent.setup()
    mockApi({ role: 'Employee', requests: [] })
    render(<App />)
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'New Request' }))
    await fillMaterialPurchaseRequest(user, 'Toast timing test')
    await user.click(screen.getByRole('button', { name: /Submit request/ }))

    expect(await screen.findByText('Your request has been submitted.')).toBeTruthy()
    await new Promise((resolve) => window.setTimeout(resolve, 4100))
    expect(screen.queryByText('Your request has been submitted.')).toBeNull()
  }, 12000)

  it('dismisses a toast manually and clears remaining toasts on logout', async () => {
    mockApi({ role: 'Employee', requests: [] })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'employee@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'New Request' }))
    await fillMaterialPurchaseRequest(user, 'Toast dismissal test')
    await user.click(screen.getByRole('button', { name: /Submit request/ }))
    expect(await screen.findByText('Your request has been submitted.')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Dismiss success notification' }))
    expect(screen.queryByText('Your request has been submitted.')).toBeNull()

    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'My Requests' }))
    await user.click(within(screen.getByRole('navigation', {
      name: 'Main navigation',
    })).getByRole('button', { name: 'New Request' }))
    await fillMaterialPurchaseRequest(user, 'Toast logout test')
    await user.click(screen.getByRole('button', { name: /Submit request/ }))
    expect(await screen.findByText('Your request has been submitted.')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(screen.queryByText('Your request has been submitted.')).toBeNull()
  }, 15000)

  it('clears the authenticated session on sign out', async () => {
    mockApi()
    render(<App />)
    const user = await signInAsManager()
    expect(sessionStorage.getItem('flowdesk-token')).toBe('test-session')

    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    expect(sessionStorage.getItem('flowdesk-token')).toBeNull()
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeTruthy()
  })
})

describe('Employee leave cancellation', () => {
  beforeEach(() => {
    sessionStorage.clear()
    window.history.replaceState(null, '', '/')
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    setViewportWidth(1024)
    signalrMock.handlers = {}
  })

  it('submits a reasoned cancellation request for approved leave', async () => {
    const api = mockApi({
      role: 'Employee',
      requests: [{
        ...pendingRequest,
        id: 114,
        title: 'Annual Leave request',
        status: 'Approved',
        requestTypeCode: 'LEAVE',
        requestTypeName: 'Leave Request',
        requestDetails: {
          leaveType: 'Annual Leave',
          startDate: '2026-11-02',
          endDate: '2026-11-02',
          reason: 'Personal time.',
          totalLeaveDays: 1,
          calculatedLeaveUnits: 0.5,
        },
      }],
    })
    render(<App />)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText('Email address'), 'orhan@example.test')
    await user.type(screen.getByLabelText('Password'), 'local-test-password')
    await user.click(screen.getByRole('button', { name: /Sign in/ }))
    await screen.findByRole('heading', { name: /Good (morning|afternoon|evening), Ali/ })
    await user.click(screen.getByRole('button', { name: 'My Requests' }))
    await user.click(await screen.findByRole('button', { name: 'View' }))
    await user.click(screen.getByRole('button', { name: 'Request leave cancellation' }))
    await user.type(screen.getByLabelText('Cancellation reason'), 'Plans changed.')
    await user.click(screen.getByRole('button', { name: 'Submit cancellation request' }))

    await waitFor(() => expect(api.calls).toContainEqual({
      method: 'POST',
      path: '/api/requests/114/cancellation-requests',
      body: JSON.stringify({ reason: 'Plans changed.' }),
    }))
    expect(await screen.findByText('Cancellation request submitted for approval.')).toBeTruthy()
    expect(screen.queryByRole('dialog', { name: 'Annual Leave request' })).toBeNull()
  })
})
