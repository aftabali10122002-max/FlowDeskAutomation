/* @vitest-environment jsdom */
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const pendingRequest = {
  id: 317,
  title: 'Laptop replacement',
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

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function mockApi(options: {
  decisionStatus?: number
  loginStatus?: number
  requestCreateStatus?: number
  requestsStatus?: number
  requests?: typeof pendingRequest[]
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
  users?: { id: number; fullName: string; email: string; role: string; departmentId: number | null; createdAtUtc: string }[]
  departments?: { id: number; name: string; code: string; isActive: boolean; createdAtUtc: string }[]
  workflowRules?: unknown[]
} = {}) {
  let requests = options.requests ?? [{ ...pendingRequest }]
  const role = options.role ?? 'Manager'
  let users = options.users ?? []
  const departments = options.departments ?? []
  let workflowRules = options.workflowRules ?? []
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
      return jsonResponse(200, {
        userId: '42',
        fullName: 'Ali Manager',
        email: 'ali.manager@example.test',
        role,
      })
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
        title: string
        amount: number
        priority: 'Low' | 'Medium' | 'High' | 'Urgent'
      }
      const createdRequest = {
        ...pendingRequest,
        id: 502,
        title: body.title,
        amount: body.amount,
        priority: body.priority,
        status: 'Pending',
        submittedBy: 'Ali Manager',
        assignments: [],
      }
      requests = [...requests, createdRequest]
      return jsonResponse(201, createdRequest)
    }
    if (url.pathname.match(/^\/api\/requests\/\d+\/workflow$/) && method === 'GET') {
      return options.workflow
        ? jsonResponse(200, options.workflow)
        : jsonResponse(404, { message: 'Workflow not found.' })
    }
    if (url.pathname === '/api/admin/users' && method === 'GET') {
      return jsonResponse(200, users)
    }
    if (url.pathname === '/api/admin/departments' && method === 'GET') {
      return jsonResponse(200, departments)
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
  return { calls }
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

describe('Manager dashboard', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
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
    expect(within(dialog).getByText(/Assigned to Ali Manager/)).toBeTruthy()
    expect(within(dialog).getByText(/legacy manual-assignment workflow/)).toBeTruthy()
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

    expect(screen.getByText('Request 1')).toBeTruthy()
    expect(screen.queryByText('Request 9')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByText('Request 9')).toBeTruthy()
    expect(screen.getByText('Page 2 of 2')).toBeTruthy()
  })

  it('shows Admin management navigation and assigns a pending request to an eligible same-department Manager', async () => {
    mockApi({
      role: 'Admin',
      requests: [{ ...pendingRequest, assignments: [] }],
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
    await user.click(await screen.findByRole('button', { name: 'Assign Manager' }))

    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Manager'), '88')
    await user.click(within(dialog).getByRole('button', { name: 'Assign Manager' }))

    await waitFor(() => {
      expect(screen.getByText('Manager assigned successfully.')).toBeTruthy()
    })
    expect(screen.getByText('Assigned')).toBeTruthy()
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
    await user.click(screen.getByRole('button', { name: 'New Request' }))

    await user.type(screen.getByLabelText(/Request title/), 'Laptop replacement')
    await user.type(screen.getByLabelText(/Amount/), '1250')
    await user.click(screen.getByRole('button', { name: /Submit request/ }))

    expect(await screen.findByText('Your request has been submitted.')).toBeTruthy()
    expect(screen.getByText('Laptop replacement')).toBeTruthy()
    const createCall = calls.find((call) => call.method === 'POST' && call.path === '/api/requests')
    expect(createCall?.body).toBe(JSON.stringify({
      title: 'Laptop replacement',
      amount: 1250,
      priority: 'Medium',
    }))
  })

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
