export type RequestItem = {
  id: number
  title: string
  description?: string | null
  amount: number | null
  requestTypeId?: number | null
  requestTypeCode?: string | null
  requestTypeName?: string | null
  requestDetails?: RequestTypeDetails | null
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

export type RequestAttachmentItem = {
  id: number
  requestId: number
  fileName: string
  extension: string
  mediaType: string
  sizeBytes: number
  uploadedAtUtc: string
  uploadedBy: string
  scanStatus: 'Pending' | 'Clean' | 'Rejected' | 'NotScanned'
  isAvailable: boolean
}

export type AuditLogItem = {
  id: number
  actorUserId: number
  actorName: string | null
  action: string
  entityType: string
  entityId: number
  occurredAtUtc: string
  details: string | null
}

export type AuditLogPage = {
  items: AuditLogItem[]
  page: number
  pageSize: number
  totalCount: number
}

export type DashboardAnalytics = {
  role: 'Admin' | 'Manager' | 'Employee'
  from: string
  to: string
  totalRequests: number
  statusDistribution: { label: string; count: number }[]
  submissionTrend: { date: string; count: number }[]
  departmentPerformance: {
    department: string
    approved: number
    pending: number
    rejected: number
  }[]
  approvalPerformance: {
    department: string
    averageHours: number
    decisions: number
  }[]
  requestTypes: { code: string; name: string; count: number }[]
  assignedRequestTrend: { date: string; count: number }[]
  managerRequestStatus: { label: string; count: number }[]
  decisionActivity: { date: string; count: number }[]
  averageDecisionTimeHours: number | null
  priorityDistribution: { label: string; count: number }[]
  approvalProgress: { label: string; count: number }[]
  adminSummary: {
    totalRequests: number
    pendingRequests: number
    approvedRequests: number
    rejectedRequests: number
    cancelledRequests: number
    approvalRatePercent: number | null
    averageApprovalTimeHours: number | null
    overdueRequests: number
    activeEmployees: number
    activeManagers: number
  } | null
  managerSummary: {
    assignedRequests: number
    pendingApprovals: number
    approvedRequests: number
    rejectedRequests: number
    overdueApprovals: number
    averageDecisionTimeHours: number | null
  } | null
  employeeSummary: {
    totalRequests: number
    pendingRequests: number
    approvedRequests: number
    rejectedRequests: number
    cancelledRequests: number
  } | null
  workflowBottlenecks: {
    stage: string
    pendingApprovals: number
    overdueApprovals: number
    averageAgeHours: number
  }[]
}

export type ReportQuery = {
  from?: string
  to?: string
  page?: number
  pageSize?: number
  sortBy?: string
  sortDirection?: 'asc' | 'desc'
  search?: string
  departmentId?: number
  requestTypeId?: number
  status?: string
  priority?: string
  employeeUserId?: number
  managerUserId?: number
}

export type ReportPage<T> = {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export type RequestReportRow = {
  requestId: number
  title: string
  status: string
  requestTypeCode: string | null
  requestTypeName: string | null
  priority: string | null
  createdAtUtc: string
  employeeUserId: number | null
  employeeName: string | null
  departmentId: number | null
  departmentName: string | null
  amount: number | null
}

export type ApprovalHistoryReportRow = {
  recordId: number
  source: 'Workflow' | 'Manual'
  requestId: number
  requestTitle: string
  requestStatus: string
  employeeUserId: number | null
  employeeName: string | null
  departmentName: string | null
  managerUserId: number
  managerName: string
  stageSequence: number | null
  decision: string
  assignedAtUtc: string
  decidedAtUtc: string
  comment: string | null
}

export type WorkflowPerformanceReportRow = {
  requestId: number
  requestTitle: string
  workflowName: string
  workflowVersion: number
  workflowStatus: string
  requestStatus: string
  stageCount: number
  approvedStageCount: number
  rejectedStageCount: number
  activeStageCount: number
  startedAtUtc: string
  completedAtUtc: string | null
  elapsedHours: number | null
  employeeUserId: number | null
  employeeName: string | null
  departmentName: string | null
}

export type LeaveReportRow = {
  requestId: number
  requestStatus: string
  priority: string | null
  createdAtUtc: string
  employeeUserId: number
  employeeName: string
  departmentId: number | null
  departmentName: string | null
  leaveTypeId: number | null
  leaveType: string
  startDate: string
  endDate: string
  totalLeaveDays: number
  calculatedLeaveUnits: number | null
  earned: number | null
  used: number | null
  pending: number | null
  scheduled: number | null
  available: number | null
}

export type ReportFilterOptions = {
  departments: { id: number; name: string }[]
  employees: { id: number; name: string }[]
  managers: { id: number; name: string }[]
  requestTypes: { id: number; name: string }[]
}

function reportQueryString(query: ReportQuery) {
  const params = new URLSearchParams()
  Object.entries(query).forEach(([key, value]) => {
    if (value !== undefined && value !== '') params.set(key, String(value))
  })
  return params.toString()
}

export function getReportFilterOptions() {
  return request<ReportFilterOptions>('/reports/filter-options')
}

export function getRequestReport(query: ReportQuery) {
  return request<ReportPage<RequestReportRow>>(
    `/reports/requests?${reportQueryString(query)}`,
  )
}

export function getApprovalHistoryReport(query: ReportQuery) {
  return request<ReportPage<ApprovalHistoryReportRow>>(
    `/reports/approval-history?${reportQueryString(query)}`,
  )
}

export function getWorkflowPerformanceReport(query: ReportQuery) {
  return request<ReportPage<WorkflowPerformanceReportRow>>(
    `/reports/workflow-performance?${reportQueryString(query)}`,
  )
}

export function getLeaveReport(query: ReportQuery) {
  return request<ReportPage<LeaveReportRow>>(
    `/reports/leave?${reportQueryString(query)}`,
  )
}

export type ReportExportName =
  | 'requests'
  | 'approval-history'
  | 'workflow-performance'
  | 'leave'

export type ReportExportFormat = 'xlsx' | 'pdf'

export async function downloadReportExport(
  report: ReportExportName,
  query: ReportQuery,
  format: ReportExportFormat,
) {
  const token = sessionStorage.getItem('flowdesk-token')
  const headers = new Headers({ Accept: format === 'pdf'
    ? 'application/pdf'
    : 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(
    `/api/reports/${report}/export?format=${format}&${reportQueryString(query)}`,
    { headers },
  )
  if (response.status === 401) sessionStorage.removeItem('flowdesk-token')
  if (!response.ok) {
    const body = await response.json().catch(() => null) as {
      message?: string
      title?: string
      detail?: string
    } | null
    const message = body?.message ?? body?.detail ?? body?.title ??
      (response.status === 401
        ? 'Your session has expired. Please sign in again.'
        : response.status === 403
          ? 'You do not have permission to export this report.'
          : `Report export failed (${response.status}).`)
    throw new ApiError(response.status, message)
  }

  const disposition = response.headers.get('Content-Disposition') ?? ''
  const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
  const plainName = disposition.match(/filename="?([^";]+)"?/i)?.[1]
  const fallbackName = `FlowDesk_${report.replaceAll('-', '_')}_${new Date().toISOString().slice(0, 10)}.${format}`
  return {
    blob: await response.blob(),
    fileName: encodedName
      ? decodeURIComponent(encodedName)
      : plainName ?? fallbackName,
  }
}

export type RequestTypeDetails = {
  itemName?: string
  quantity?: number
  businessJustification?: string
  leaveType?: string
  startDate?: string
  endDate?: string
  reason?: string
  totalLeaveDays?: number
  calculatedLeaveUnits?: number
  cancellationOfRequestId?: number | null
  category?: string
  expenseDate?: string
  systemApplication?: string
  requestedAccessLevel?: string
  durationDays?: number | null
  destination?: string
  businessPurpose?: string
}

export type RequestType = {
  id: number
  code: string
  displayName: string
  requiresAmount: boolean
  isEnabled: boolean
}

export type CreateTypedRequestInput =
  | {
    requestTypeCode: 'MATERIAL_PURCHASE'
    description?: string
    amount: number
    priority: NonNullable<RequestItem['priority']>
    materialPurchase: {
      itemName: string
      quantity: number
      businessJustification: string
    }
  }
  | {
    requestTypeCode: 'LEAVE'
    description?: string
    priority: NonNullable<RequestItem['priority']>
    leave: {
      leaveType: string
      startDate: string
      endDate: string
      reason: string
      startDayPortion?: 'FullDay' | 'HalfDay'
      endDayPortion?: 'FullDay' | 'HalfDay'
    }
  }
  | {
    requestTypeCode: 'EXPENSE_REIMBURSEMENT'
    description?: string
    amount: number
    priority: NonNullable<RequestItem['priority']>
    expenseReimbursement: {
      category: string
      expenseDate: string
      businessJustification: string
    }
  }
  | {
    requestTypeCode: 'IT_ACCESS'
    description?: string
    priority: NonNullable<RequestItem['priority']>
    itAccess: {
      systemApplication: string
      requestedAccessLevel: string
      durationDays: number | null
      businessJustification: string
    }
  }
  | {
    requestTypeCode: 'TRAVEL'
    description?: string
    amount: number
    priority: NonNullable<RequestItem['priority']>
    travel: {
      destination: string
      startDate: string
      endDate: string
      businessPurpose: string
    }
  }
  | {
    requestTypeCode: 'WORK_FROM_HOME'
    description?: string
    priority: NonNullable<RequestItem['priority']>
    workFromHome: {
      startDate: string
      endDate: string
      reason: string
    }
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
  requestTypeId: number | null
  requestTypeCode: string | null
  requestTypeName: string | null
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
  requestTypeId: number | null
  departmentId: number | null
  minimumAmount: number
  maximumAmountExclusive: number | null
  priority: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  stages: WorkflowRuleStage[]
}

export type WorkflowNotification = {
  id: number
  requestId: number | null
  type: string
  message: string
  createdAtUtc: string
  readAtUtc: string | null
}

export type WorkflowNotificationPage = {
  items: WorkflowNotification[]
  totalCount: number
  unreadCount: number
  page: number
  pageSize: number
}

export type LeaveTypeInfo = {
  id: number
  code: string
  displayName: string
  isPaid: boolean
  isEnabled: boolean
}

export type LeaveBalance = {
  employeeUserId: number
  leaveTypeId: number
  leaveTypeCode: string
  leaveTypeName: string
  earned: number
  adjustments: number
  carryForward: number
  used: number
  pending: number
  scheduled: number
  expired: number
  available: number
}

export type LeaveLedgerItem = {
  id: number
  leaveTypeId: number
  leaveTypeCode: string
  leaveTypeName: string
  entryType: string
  units: number
  effectiveDate: string
  createdAtUtc: string
  expiresOn: string | null
  policyId: number | null
  requestId: number | null
  reason: string | null
  actorUserId: number | null
}

export type LeaveTeamBalance = {
  employeeUserId: number
  employeeName: string
  balances: LeaveBalance[]
}

export type LeavePolicy = {
  id: number
  code: string
  name: string
  version: number
  status: string
  scope: string
  effectiveFrom: string
  effectiveTo: string | null
  entitlements: { leaveTypeId: number; annualEntitlement: number }[]
}

export type LeavePolicyDraftInput = {
  code: string
  name: string
  version: number
  scope: 'Company' | 'Department' | 'Designation' | 'EmploymentType'
  departmentId: number | null
  designationId: number | null
  employmentType: string | null
  effectiveFrom: string
  effectiveTo: string | null
  dayCalculationMethod: 'CalendarDays' | 'WorkingDays'
  leaveWorkScheduleId: number | null
  leaveHolidayCalendarId: number | null
  allowsHalfDays: boolean
  halfDayUnits: number | null
  isAccrualEnabled: boolean
  accrualFrequency: 'Annually' | 'Monthly' | 'PerPayPeriod' | null
  allowsCarryForward: boolean
  carryForwardCap: number | null
  carryForwardExpiryMonths: number | null
  probationPeriodDays: number | null
  allowsLeaveDuringProbation: boolean
  allowsNegativeBalance: boolean
  leavePayrollCalendarId: number | null
  entitlements: { leaveTypeId: number; annualEntitlement: number }[]
}

export type LeavePreview = {
  totalUnits: number
  days: { date: string; units: number; policyId: number; policyVersion: number }[]
}

export type LeaveSchedule = {
  id: number
  code: string
  name: string
  isEnabled: boolean
  days: { dayOfWeek: number; isWorkingDay: boolean; standardHours: number }[]
}

export type LeaveDesignation = {
  id: number
  code: string
  name: string
  isEnabled: boolean
}

export type LeaveHolidayCalendar = {
  id: number
  code: string
  name: string
  region: string | null
  isEnabled: boolean
  holidays: { id: number; date: string; name: string }[]
}

export type LeavePayrollCalendar = {
  id: number
  code: string
  name: string
  timeZoneId: string
  expectedPeriodsInEntitlementYear: number
  isEnabled: boolean
  periods: {
    id: number
    startDate: string
    endDate: string
    postingDate: string
    entitlementYear: number
    periodNumber: number
  }[]
}

export type EmployeeLeaveMetadata = {
  employeeUserId: number
  fullName: string
  role: string
  departmentId: number | null
  departmentName: string | null
  designationId: number | null
  designationName: string | null
  employmentType: string | null
  hireDate: string | null
  effectiveFrom: string
  effectiveTo: string | null
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
  hasProfileImage?: boolean
  profileImageUrl?: string | null
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
  departmentId: number | null
  departmentName: string | null
  phoneNumber: string | null
  hasProfileImage: boolean
  profileImageUrl: string
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
  if (init.body && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }
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

function sendMultipart<T>(
  path: string,
  body: FormData,
  onProgress?: (progress: number) => void,
) {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', path)
    const token = sessionStorage.getItem('flowdesk-token')
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress?.(Math.round((event.loaded / event.total) * 100))
      }
    }
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        onProgress?.(100)
        try {
          resolve(JSON.parse(xhr.responseText) as T)
        } catch {
          reject(new ApiError(
            xhr.status,
            'The server returned an invalid upload response.',
          ))
        }
        return
      }

      if (xhr.status === 401) sessionStorage.removeItem('flowdesk-token')
      let message = `Upload failed (${xhr.status}).`
      try {
        const body = JSON.parse(xhr.responseText) as {
          message?: string
          detail?: string
          title?: string
        }
        message = body.message ?? body.detail ?? body.title ?? message
      } catch {
        message = xhr.status === 401
          ? 'Your session has expired. Please sign in again.'
          : message
      }
      reject(new ApiError(xhr.status, message))
    }
    xhr.onerror = () => reject(new ApiError(0, 'The upload could not reach the server.'))
    xhr.onabort = () => reject(new ApiError(0, 'The upload was cancelled.'))
    xhr.send(body)
  })
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

export function updateMyProfile(fullName: string, phoneNumber: string) {
  return request<UserProfile>('/profile/me', {
    method: 'PATCH',
    body: JSON.stringify({ fullName, phoneNumber }),
  })
}

export function uploadMyProfilePhoto(image: File) {
  const form = new FormData()
  form.append('image', image)
  return request<UserProfile>('/profile/me/photo', {
    method: 'PUT',
    body: form,
  })
}

export function removeMyProfilePhoto() {
  return request<void>('/profile/me/photo', { method: 'DELETE' })
}

export function getRequests() {
  return request<RequestItem[]>('/requests')
}

export function getAuditLogs(filters: {
  page: number
  pageSize: number
  action?: string
  entityType?: string
  fromUtc?: string
  toUtc?: string
}) {
  const query = new URLSearchParams({
    page: String(filters.page),
    pageSize: String(filters.pageSize),
  })
  if (filters.action) query.set('action', filters.action)
  if (filters.entityType) query.set('entityType', filters.entityType)
  if (filters.fromUtc) query.set('fromUtc', filters.fromUtc)
  if (filters.toUtc) query.set('toUtc', filters.toUtc)
  return request<AuditLogPage>(`/admin/audit-logs?${query.toString()}`)
}

export function getDashboardAnalytics(
  from: string,
  to: string,
  signal?: AbortSignal,
) {
  const query = new URLSearchParams({ from, to })
  return request<DashboardAnalytics>(
    `/analytics/dashboard?${query.toString()}`,
    { signal },
  )
}

export function getRequest(id: number) {
  return request<RequestItem>(`/requests/${id}`)
}

export function createRequest(
  title: string,
  amount: number,
  priority: NonNullable<RequestItem['priority']>,
  description?: string,
) {
  return request<RequestItem>('/requests', {
    method: 'POST',
    body: JSON.stringify({ title, description, amount, priority }),
  })
}

export function createTypedRequest(input: CreateTypedRequestInput) {
  return request<RequestItem>('/requests', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function createRequestWithAttachments(
  input: CreateTypedRequestInput,
  files: File[],
  onProgress?: (progress: number) => void,
  clientUploadIds?: string[],
) {
  const form = new FormData()
  form.append('request', JSON.stringify(input))
  files.forEach((file, index) => {
    form.append('files', file)
    form.append(
      'clientUploadIds',
      clientUploadIds?.[index] ?? crypto.randomUUID(),
    )
  })

  return sendMultipart<{ id: number }>(
    '/api/requests/with-attachments',
    form,
    onProgress,
  )
}

export function getRequestAttachments(requestId: number) {
  return request<RequestAttachmentItem[]>(
    `/requests/${requestId}/attachments`,
  )
}

export function uploadRequestAttachment(
  requestId: number,
  file: File,
  onProgress?: (progress: number) => void,
  clientUploadId?: string,
) {
  const form = new FormData()
  form.append('file', file)
  form.append('clientUploadId', clientUploadId ?? crypto.randomUUID())
  return sendMultipart<RequestAttachmentItem>(
    `/api/requests/${requestId}/attachments`,
    form,
    onProgress,
  )
}

export function downloadRequestAttachment(
  requestId: number,
  attachmentId: number,
  preview = false,
) {
  const path = `/api/requests/${requestId}/attachments/${attachmentId}${preview ? '/preview' : ''}`
  const headers = new Headers()
  const token = sessionStorage.getItem('flowdesk-token')
  if (token) headers.set('Authorization', `Bearer ${token}`)

  return fetch(path, { headers, cache: 'no-store' }).then(async (response) => {
    if (!response.ok) {
      const body = await response.json().catch(() => null) as {
        message?: string
        detail?: string
      } | null
      throw new ApiError(
        response.status,
        body?.message ?? body?.detail ?? `Download failed (${response.status}).`,
      )
    }

    return response.blob()
  })
}

export function removeRequestAttachment(
  requestId: number,
  attachmentId: number,
) {
  return request<void>(
    `/requests/${requestId}/attachments/${attachmentId}`,
    { method: 'DELETE' },
  )
}

export function getRequestTypes() {
  return request<RequestType[]>('/request-types')
}

export function getAdminRequestTypes() {
  return request<RequestType[]>('/admin/request-types')
}

export function setRequestTypeEnabled(id: number, isEnabled: boolean) {
  return request<RequestType>(`/admin/request-types/${id}/enabled`, {
    method: 'PATCH',
    body: JSON.stringify({ isEnabled }),
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

export function createLeaveCancellationRequest(id: number, reason: string) {
  return request<RequestItem>(`/requests/${id}/cancellation-requests`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
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

export function getNotifications(page = 1, pageSize = 20) {
  return request<WorkflowNotificationPage>(
    `/notifications?page=${page}&pageSize=${pageSize}`,
  )
}

export function markNotificationRead(id: number) {
  return request<void>(`/notifications/${id}/read`, { method: 'PATCH' })
}

export function getLeaveTypes() {
  return request<LeaveTypeInfo[]>('/leave/types')
}

export function getAdminLeaveTypes() {
  return request<LeaveTypeInfo[]>('/admin/leave/types')
}

export function createAdminLeaveType(input: {
  code: string
  displayName: string
  isPaid: boolean
}) {
  return request<LeaveTypeInfo>('/admin/leave/types', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function updateAdminLeaveType(
  id: number,
  input: { displayName: string; isPaid: boolean; isEnabled: boolean },
) {
  return request<LeaveTypeInfo>(`/admin/leave/types/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function getMyLeaveBalances(asOf?: string) {
  const query = asOf ? `?asOf=${encodeURIComponent(asOf)}` : ''
  return request<LeaveBalance[]>(`/leave/me/balances${query}`)
}

export function getMyLeaveHistory() {
  return request<LeaveLedgerItem[]>('/leave/me/history')
}

export function getTeamLeaveBalances() {
  return request<LeaveTeamBalance[]>('/leave/manager/team')
}

export function previewMyLeave(input: {
  leaveType: string
  startDate: string
  endDate: string
  startDayPortion: 'FullDay' | 'HalfDay'
  endDayPortion: 'FullDay' | 'HalfDay'
}) {
  return request<LeavePreview>('/leave/me/preview', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function getAdminLeavePolicies() {
  return request<LeavePolicy[]>('/admin/leave/policies')
}

export function createAdminLeavePolicy(input: LeavePolicyDraftInput) {
  return request<LeavePolicy>('/admin/leave/policies', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function publishAdminLeavePolicy(id: number) {
  return request<void>(`/admin/leave/policies/${id}/publish`, {
    method: 'POST',
  })
}

export function assignLeavePolicy(input: {
  employeeUserId: number
  leavePolicyId: number
  effectiveFrom: string
  effectiveTo: string | null
}) {
  return request<void>('/admin/leave/assignments', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function getEmployeeLeaveMetadata(employeeUserId: number) {
  return request<EmployeeLeaveMetadata[]>(
    `/admin/leave/employees/${employeeUserId}/metadata`,
  )
}

export function saveEmployeeLeaveMetadata(input: {
  employeeUserId: number
  designationId: number | null
  employmentType: string | null
  hireDate: string | null
  effectiveFrom: string
  effectiveTo: string | null
}) {
  return request<void>('/admin/leave/employees/metadata', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function getEmployeeLeaveBalances(employeeUserId: number) {
  return request<LeaveBalance[]>(
    `/admin/leave/employees/${employeeUserId}/balances`,
  )
}

export function createLeaveAdjustment(input: {
  employeeUserId: number
  leaveTypeId: number
  units: number
  effectiveDate: string
  reason: string
  idempotencyKey: string
}) {
  return request<void>('/admin/leave/adjustments', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function getLeaveAdjustmentAudit(employeeUserId: number) {
  return request<LeaveLedgerItem[]>(
    `/admin/leave/adjustments?employeeUserId=${employeeUserId}`,
  )
}

export function getLeaveWorkSchedules() {
  return request<LeaveSchedule[]>('/admin/leave/work-schedules')
}

export function createLeaveWorkSchedule(input: {
  code: string
  name: string
  days: LeaveSchedule['days']
}) {
  return request<LeaveSchedule>('/admin/leave/work-schedules', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function getLeaveHolidayCalendars() {
  return request<LeaveHolidayCalendar[]>('/admin/leave/holiday-calendars')
}

export function createLeaveHolidayCalendar(input: {
  code: string
  name: string
  region: string | null
}) {
  return request<LeaveHolidayCalendar>('/admin/leave/holiday-calendars', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function addLeaveHoliday(
  calendarId: number,
  input: { date: string; name: string },
) {
  return request<LeaveHolidayCalendar['holidays'][number]>(
    `/admin/leave/holiday-calendars/${calendarId}/holidays`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
  )
}

export function getLeavePayrollCalendars() {
  return request<LeavePayrollCalendar[]>('/admin/leave/payroll-calendars')
}

export function createLeavePayrollCalendar(input: {
  code: string
  name: string
  timeZoneId: string
  expectedPeriodsInEntitlementYear: number
}) {
  return request<LeavePayrollCalendar>('/admin/leave/payroll-calendars', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function addLeavePayPeriod(
  calendarId: number,
  input: {
    startDate: string
    endDate: string
    postingDate: string
    entitlementYear: number
    periodNumber: number
  },
) {
  return request<LeavePayrollCalendar['periods'][number]>(
    `/admin/leave/payroll-calendars/${calendarId}/periods`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
  )
}

export function getLeaveDesignations() {
  return request<LeaveDesignation[]>(
    '/admin/leave/designations',
  )
}

export function createLeaveDesignation(input: { code: string; name: string }) {
  return request<LeaveDesignation>('/admin/leave/designations', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}
