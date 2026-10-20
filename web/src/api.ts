export type RequestItem = {
  id: number
  title: string
  amount: number
  priority: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  status: string
  createdAtUtc: string
  createdByUserId?: number | null
  createdByName?: string | null
  createdByEmail?: string | null
  submittedBy?: string | null
  departmentId?: number | null
  departmentName?: string | null
  department?: string | null
  assignments?: RequestAssignment[]
}

export type WorkflowStage = {
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
}

export type WorkflowInstance = {
  requestId: number
  workflowName: string
  workflowVersion: number
  status: string
  startedAtUtc: string
  completedAtUtc: string | null
  stages: WorkflowStage[]
}

export type WorkflowRuleStage = {
  sequence: number
  approverType: 'RequestDepartmentManager' | 'DepartmentManager' | 'SpecificManager'
  approverDepartmentId: number | null
  specificApproverUserId: number | null
  deadlineMinutes: number | null
  escalationAfterMinutes: number | null
  escalationApproverUserId: number | null
}

export type WorkflowRule = {
  id: number
  workflowKey: string
  name: string
  version: number
  departmentId: number | null
  departmentName: string | null
  minimumAmount: number
  maximumAmountExclusive: number | null
  priority: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  status: 'Draft' | 'Active' | 'Inactive'
  createdAtUtc: string
  publishedAtUtc: string | null
  stages: WorkflowRuleStage[]
}

export type WorkflowRuleInput = {
  workflowKey: string
  name: string
  departmentId: number | null
  minimumAmount: number
  maximumAmountExclusive: number | null
  priority: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  stages: WorkflowRuleStage[]
}

export type WorkflowNotification = {
  id: number
  message: string
  createdAtUtc: string
  readAtUtc: string | null
}

export type RequestAssignment = {
  id: number
  approverName: string
  approverEmail?: string | null
  status: string
  assignedAtUtc: string
  decidedAtUtc: string | null
}

export type User = {
  id: number
  fullName: string
  email: string
  role: string
  isActive: boolean
  departmentId: number | null
  createdAtUtc: string
}

export type Department = {
  id: number
  name: string
  code: string | null
  isActive: boolean
  createdAtUtc: string
}

export type UserProfile = {
  userId: string | number
  fullName: string
  email: string
  role: string
}

type LoginResponse = {
  id: number
  accessToken: string
  expiresAtUtc: string
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
    this.name = 'ApiError'
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = sessionStorage.getItem('flowdesk-token')
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body) headers.set('Content-Type', 'application/json')
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(`/api${path}`, { ...init, headers })
  if (response.status === 401) sessionStorage.removeItem('flowdesk-token')
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { message?: string; title?: string; detail?: string } | null
    const message = body?.message ?? body?.detail ?? body?.title ?? (response.status === 401
      ? 'Your session has expired. Please sign in again.'
      : response.status === 403
        ? 'You do not have permission to access this resource.'
        : `Request failed (${response.status}).`)
    throw new ApiError(response.status, message)
  }
  const responseBody = await response.text()
  return responseBody ? JSON.parse(responseBody) as T : undefined as T
}

export function login(email: string, password: string) {
  return request<LoginResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  })
}

export function getMe() {
  return request<UserProfile>('/auth/me')
}

export function getRequests() {
  return request<RequestItem[]>('/requests')
}

export function getRequest(id: number) {
  return request<RequestItem>(`/requests/${id}`)
}

export function createRequest(
  title: string,
  amount: number,
  priority: NonNullable<RequestItem['priority']>,
) {
  return request<RequestItem>('/requests', {
    method: 'POST',
    body: JSON.stringify({ title, amount, priority }),
  })
}

export function getRequestWorkflow(id: number): Promise<WorkflowInstance | null> {
  return request<WorkflowInstance>(`/requests/${id}/workflow`).catch((error: unknown) => {
    if (error instanceof ApiError && error.status === 404) return null
    throw error
  })
}

export function patchRequest(
  id: number,
  action: 'approve' | 'reject' | 'cancel',
  comment?: string,
) {
  return request<void>(`/requests/${id}/${action}`, {
    method: 'PATCH',
    ...(comment ? { body: JSON.stringify({ comment }) } : {}),
  })
}

export function assignRequest(id: number, approverUserId: number) {
  return request<void>(`/requests/${id}/assignments`, {
    method: 'POST',
    body: JSON.stringify({ approverUserId }),
  })
}

export function getUsers() {
  return request<User[]>('/admin/users')
}

export function getUser(id: number) {
  return request<User>(`/admin/users/${id}`)
}

export function createAdminUser(input: {
  fullName: string
  email: string
  password: string
  role: 'Employee' | 'Manager'
  departmentId: number
}) {
  return request<User>('/admin/users', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function updateUserRole(id: number, role: string) {
  return request<User>(`/admin/users/${id}/role`, {
    method: 'PATCH',
    body: JSON.stringify({ role }),
  })
}

export function updateUserDepartment(id: number, departmentId: number | null) {
  return request<User>(`/admin/users/${id}/department`, {
    method: 'PATCH',
    body: JSON.stringify({ departmentId }),
  })
}

export function updateUserActive(id: number, isActive: boolean) {
  return request<User>(`/admin/users/${id}/active`, {
    method: 'PATCH',
    body: JSON.stringify({ isActive }),
  })
}

export function getDepartments() {
  return request<Department[]>('/admin/departments')
}

export function createDepartment(name: string, code: string) {
  return request<Department>('/admin/departments', {
    method: 'POST',
    body: JSON.stringify({ name, code: code || null }),
  })
}

export function getWorkflowRules() {
  return request<WorkflowRule[]>('/admin/workflows')
}

export function createWorkflowRule(input: WorkflowRuleInput) {
  return request<WorkflowRule>('/admin/workflows', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function updateWorkflowDraft(id: number, input: WorkflowRuleInput) {
  return request<WorkflowRule>(`/admin/workflows/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function createWorkflowVersion(id: number) {
  return request<WorkflowRule>(`/admin/workflows/${id}/versions`, {
    method: 'POST',
  })
}

export function activateWorkflow(id: number) {
  return request<void>(`/admin/workflows/${id}/activate`, { method: 'POST' })
}

export function deactivateWorkflow(id: number) {
  return request<void>(`/admin/workflows/${id}/deactivate`, { method: 'POST' })
}

export function processWorkflowEscalations() {
  return request<{ processed: number }>('/admin/workflows/process-escalations', {
    method: 'POST',
  })
}

export function getNotifications() {
  return request<WorkflowNotification[]>('/notifications')
}

export function markNotificationRead(id: number) {
  return request<void>(`/notifications/${id}/read`, { method: 'PATCH' })
}
