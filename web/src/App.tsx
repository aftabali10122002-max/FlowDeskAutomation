import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent, ReactNode, RefObject } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import {
  ApiError,
  assignRequest,
  createAdminUser,
  createDepartment,
  createLeaveCancellationRequest,
  createTypedRequest,
  createRequestWithAttachments,
  getDepartments,
  getMe,
  getNotifications,
  getRequest,
  getRequestWorkflow,
  getRequests,
  getUsers,
  getUser,
  login,
  markNotificationRead,
  patchRequest,
  getRequestTypes,
  removeMyProfilePhoto,
  uploadMyProfilePhoto,
  updateUserRole,
  updateUserDepartment,
  updateUserActive,
  type Department,
  type RequestType,
  type CreateTypedRequestInput,
  type RequestItem,
  type User,
  type UserProfile,
  type WorkflowInstance,
  type WorkflowNotification,
} from './api'
import { WorkflowManagement } from './WorkflowManagement'
import { RequestTypeManagement } from './RequestTypeManagement'
import { LeaveManagement } from './LeaveManagement'
import { Reports } from './Reports'
import { AuditLogs } from './AuditLogs'
import { DashboardAnalyticsProvider } from './DashboardAnalyticsData'
import { AttachmentPicker, RequestAttachmentsPanel } from './RequestAttachments'
import { formatDate } from './dateFormatting'
import './App.css'
import './RequestTable.css'

const DashboardAnalytics = lazy(() => import('./DashboardAnalytics').then((module) => ({
  default: module.DashboardAnalytics,
})))

type Page = 'Overview' | 'Analytics' | 'Reports' | 'My Leave' | 'Team Leave' | 'Leave Management' | 'My Requests' | 'New Request' | 'Assigned to Me' | 'Request History' | 'User Management' | 'Departments' | 'All Requests' | 'Assignment Management' | 'Workflow Management' | 'Request Types' | 'Audit Logs' | 'Profile'
type ToastMessage = {
  id: number
  kind: 'success' | 'error' | 'warning' | 'info'
  text: string
}

function useDialogEscape(
  dialogRef: RefObject<HTMLElement | null>,
  onEscape: () => void,
  canEscape = true,
) {
  const onEscapeRef = useRef(onEscape)
  const canEscapeRef = useRef(canEscape)

  useEffect(() => {
    onEscapeRef.current = onEscape
  }, [onEscape])

  useEffect(() => {
    canEscapeRef.current = canEscape
  }, [canEscape])

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null
    const firstFocusable = dialogRef.current?.querySelector<HTMLElement>(
      'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])',
    )
    firstFocusable?.focus()

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') return
      const dialogs = document.querySelectorAll<HTMLElement>('[role="dialog"]')
      if (
        dialogs.item(dialogs.length - 1) !== dialogRef.current ||
        !canEscapeRef.current
      ) return
      event.preventDefault()
      onEscapeRef.current()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      previousFocus?.focus()
    }
  }, [dialogRef])
}

const rolePages: Record<string, Page[]> = {
  Employee: ['Overview', 'Analytics', 'Reports', 'My Leave', 'My Requests', 'New Request', 'Profile'],
  Manager: ['Overview', 'Analytics', 'Reports', 'Team Leave', 'Assigned to Me', 'Request History', 'Profile'],
  Admin: ['Overview', 'Analytics', 'Reports', 'Leave Management', 'All Requests', 'Assignment Management', 'Workflow Management', 'Request Types', 'User Management', 'Departments', 'Audit Logs', 'Profile'],
}

function pageHash(page: Page) {
  return `#/${page === 'Overview' ? 'dashboard' : page.toLowerCase().replaceAll(' ', '-')}`
}

function pageFromHash(): Page {
  const slug = window.location.hash.slice(2)
  const page = (Object.keys(rolePages).flatMap((role) => rolePages[role])
    .find((candidate) => pageHash(candidate).slice(2) === slug))
  return page ?? 'Overview'
}

function roleName(role: string): 'Admin' | 'Manager' | 'Employee' {
  return role.toLowerCase() === 'admin' ? 'Admin' : role.toLowerCase() === 'manager' ? 'Manager' : 'Employee'
}

function formatDateTime(date: string) {
  const parsed = new Date(date)
  return Number.isNaN(parsed.getTime())
    ? '—'
    : new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(parsed)
}

function money(amount: number | null | undefined) {
  if (amount == null) return 'Not applicable'
  return new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)
}

function messageFrom(error: unknown) {
  if (error instanceof ApiError) return error.message
  return 'Something went wrong. Please try again.'
}

function navLabel(page: Page) {
  if (page === 'Overview') return 'Dashboard'
  if (page === 'My Requests') return 'My Requests'
  if (page === 'Assigned to Me') return 'Assigned to Me'
  return page
}

function NavIcon({ page }: { page: Page }) {
  const pathByPage: Record<Page, string> = {
    Overview: 'M3 10.5 12 3l9 7.5M5 9v11h14V9M9 20v-6h6v6',
    Analytics: 'M4 19V5m0 14h16M8 15v-4m4 4V7m4 8v-6',
    Reports: 'M5 4h14v16H5zM8 8h8M8 12h8M8 16h5',
    'My Leave': 'M8 3h8l4 4v14H4V3h4Zm0 0v5h8V3m-8 10h8m-8 4h5',
    'Team Leave': 'M16 20v-1a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v1m6-9a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm7-7a4 4 0 0 1 0 7m1 4a4 4 0 0 1 3 4v1',
    'Leave Management': 'M4 4h16v16H4zM8 8h8m-8 4h8m-8 4h4',
    'My Requests': 'M8 3h8l4 4v14H4V3h4Zm0 0v5h8V3M8 13h8M8 17h5',
    'New Request': 'M12 5v14M5 12h14',
    'Assigned to Me': 'M9 12l2 2 4-4M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
    'Request History': 'M3 12a9 9 0 1 0 2.6-6.4L3 8m0-5v5h5m4-1v5l3 2',
    'User Management': 'M16 20v-1a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v1m6-9a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm7-7a4 4 0 0 1 0 7m1 4a4 4 0 0 1 3 4v1',
    Departments: 'M3 21h18M5 21V7l7-4 7 4v14M9 9h1m4 0h1m-6 4h1m4 0h1m-6 4h1m4 0h1',
    'All Requests': 'M4 5h16M4 12h16M4 19h16M8 5v0m0 7v0m0 7v0',
    'Assignment Management': 'M8 7h12M8 12h12M8 17h12M4 7h.01M4 12h.01M4 17h.01',
    'Workflow Management': 'M4 5h6v5H4zM14 14h6v5h-6zM10 7h4a2 2 0 0 1 2 2v5m-2 0 2 2 2-2',
    'Request Types': 'M4 5h16M4 12h16M4 19h16M8 5v0m0 7v0m0 7v0',
    'Audit Logs': 'M4 5h16M4 12h16M4 19h16M8 5v0m4 7v0m-4 7v0',
    Profile: 'M20 21a8 8 0 0 0-16 0m8-10a5 5 0 1 0 0-10 5 5 0 0 0 0 10Z',
  }
  return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d={pathByPage[page]} /></svg>
}

function App() {
  const [profile, setProfile] = useState<UserProfile | null>(null)
  const [token, setToken] = useState(() => sessionStorage.getItem('flowdesk-token') ?? '')
  const [page, setPage] = useState<Page>(pageFromHash)
  const [requests, setRequests] = useState<RequestItem[]>([])
  const [requestsLoadFailed, setRequestsLoadFailed] = useState(false)
  const [users, setUsers] = useState<User[]>([])
  const [departments, setDepartments] = useState<Department[]>([])
  const [requestTypes, setRequestTypes] = useState<RequestType[]>([])
  const [notificationUnreadCount, setNotificationUnreadCount] = useState(0)
  const [loading, setLoading] = useState(Boolean(sessionStorage.getItem('flowdesk-token')))
  const [error, setError] = useState('')
  const [toasts, setToasts] = useState<ToastMessage[]>([])
  const toastId = useRef(0)
  const sessionGeneration = useRef(0)
  const requestListGeneration = useRef(0)
  const requestDetailsGeneration = useRef(0)
  const [selectedRequest, setSelectedRequest] = useState<RequestItem | null>(null)
  const selectedRequestId = useRef<number | null>(null)
  const [selectedWorkflow, setSelectedWorkflow] = useState<WorkflowInstance | null>(null)
  const [assignmentTarget, setAssignmentTarget] = useState<RequestItem | null>(null)
  const [selectedUser, setSelectedUser] = useState<User | null>(null)
  const [pendingActionId, setPendingActionId] = useState<number | null>(null)
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const sidebarToggleButtonRef = useRef<HTMLButtonElement>(null)
  const mobileMenuButtonRef = useRef<HTMLButtonElement>(null)
  const sidebarRef = useRef<HTMLElement>(null)
  const navigateTo = useCallback((nextPage: Page, replace = false) => {
    setPage(nextPage)
    const method = replace ? 'replaceState' : 'pushState'
    window.history[method]({ flowDeskPage: nextPage }, '', `${window.location.pathname}${window.location.search}${pageHash(nextPage)}`)
  }, [])

  useEffect(() => {
    function syncPageFromHistory() {
      setPage(pageFromHash())
    }
    window.addEventListener('popstate', syncPageFromHistory)
    window.addEventListener('hashchange', syncPageFromHistory)
    return () => {
      window.removeEventListener('popstate', syncPageFromHistory)
      window.removeEventListener('hashchange', syncPageFromHistory)
    }
  }, [])

  useEffect(() => {
    if (!mobileMenuOpen) return
    const previousOverflow = document.body.style.overflow
    const sidebarToggleButton = sidebarToggleButtonRef.current
    const menuButton = mobileMenuButtonRef.current
    document.body.style.overflow = 'hidden'

    const sidebar = sidebarRef.current
    const focusable = sidebar?.querySelectorAll<HTMLElement>(
      'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
    )
    focusable?.item(0)?.focus()

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        setMobileMenuOpen(false)
        return
      }
      if (event.key !== 'Tab' || !focusable?.length || !sidebar) return

      const first = focusable.item(0)
      const last = focusable.item(focusable.length - 1)
      const focusIsOutsideDrawer = !sidebar.contains(document.activeElement)
      if (event.shiftKey && (document.activeElement === first || focusIsOutsideDrawer)) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && (document.activeElement === last || focusIsOutsideDrawer)) {
        event.preventDefault()
        first.focus()
      }
    }

    function closeDrawerOnDesktopResize() {
      if (window.innerWidth > 900) setMobileMenuOpen(false)
    }

    document.addEventListener('keydown', handleKeyDown)
    window.addEventListener('resize', closeDrawerOnDesktopResize)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      window.removeEventListener('resize', closeDrawerOnDesktopResize)
      document.body.style.overflow = previousOverflow
      if (window.innerWidth > 900) sidebarToggleButton?.focus()
      else menuButton?.focus()
    }
  }, [mobileMenuOpen])

  const clearSession = useCallback(() => {
    sessionGeneration.current += 1
    selectedRequestId.current = null
    requestDetailsGeneration.current += 1
    requestListGeneration.current += 1
    sessionStorage.removeItem('flowdesk-token')
    setToken('')
    setProfile(null)
    setRequests([])
    setRequestsLoadFailed(false)
    setSelectedRequest(null)
    setSelectedWorkflow(null)
    setUsers([])
    setDepartments([])
    setRequestTypes([])
    setNotificationUnreadCount(0)
    setToasts([])
    setError('')
    setLoading(false)
  }, [])

  const showToast = useCallback((
    kind: ToastMessage['kind'],
    text: string,
  ) => {
    const id = ++toastId.current
    setToasts((current) => [...current, { id, kind, text }])
  }, [])

  const handleIncomingNotification = useCallback((
    notification: WorkflowNotification,
  ) => {
    if (
      roleName(profile?.role ?? '') === 'Admin' &&
      [
        'RequestSubmitted',
        'RequestNeedsAdminAssignment',
      ].includes(notification.type)
    ) {
      showToast(
        'info',
        `New Approval Request: ${notification.message}`,
      )
    }
  }, [profile?.role, showToast])

  const dismissToast = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id))
  }, [])

  const refresh = useCallback(async (currentRole: string) => {
    const refreshSession = sessionGeneration.current
    const refreshGeneration = ++requestListGeneration.current
    setError('')
    let requestData: RequestItem[]
    try {
      requestData = await getRequests()
      if (
        refreshSession !== sessionGeneration.current ||
        refreshGeneration !== requestListGeneration.current
      ) return
      setRequestsLoadFailed(false)
    } catch (caught) {
      if (
        refreshSession === sessionGeneration.current &&
        refreshGeneration === requestListGeneration.current
      ) {
        setRequestsLoadFailed(true)
      }
      throw caught
    }
    if (
      refreshSession !== sessionGeneration.current ||
      refreshGeneration !== requestListGeneration.current
    ) return
    setRequests(requestData)
    if (roleName(currentRole) === 'Admin') {
      const [userData, departmentData] = await Promise.all([
        getUsers().catch((caught: unknown) => {
          throw new ApiError(
            caught instanceof ApiError ? caught.status : 0,
            `Could not load Admin users. ${messageFrom(caught)}`,
          )
        }),
        getDepartments().catch((caught: unknown) => {
          throw new ApiError(
            caught instanceof ApiError ? caught.status : 0,
            `Could not load departments. ${messageFrom(caught)}`,
          )
        }),
      ])
      if (refreshSession !== sessionGeneration.current) return
      setUsers(userData)
      setDepartments(departmentData)
    }
    try {
      setRequestTypes(await getRequestTypes())
    } catch (caught) {
      if (refreshSession === sessionGeneration.current) {
        setError(`Request types could not be loaded. ${messageFrom(caught)}`)
      }
    }
    try {
      const notificationPage = await getNotifications()
      if (refreshSession !== sessionGeneration.current) return
      setNotificationUnreadCount(notificationPage.unreadCount)
    } catch (caught) {
      if (refreshSession === sessionGeneration.current) {
        setError(`Notifications could not be loaded. ${messageFrom(caught)}`)
      }
    }
  }, [])

  const refreshAfterRequestNotification = useCallback(async (
    requestId: number,
    notificationType: string,
  ) => {
    const notificationSession = sessionGeneration.current
    const isTerminal = [
      'RequestApproved',
      'RequestRejected',
      'RequestCancelled',
    ].includes(notificationType)
    const currentRole = roleName(profile?.role ?? '')
    const shouldRefreshList = isTerminal ||
      (currentRole === 'Manager' &&
        ['RequestAssigned', 'StageAdvanced'].includes(notificationType)) ||
      (currentRole === 'Admin' &&
        [
          'RequestNeedsAdminAssignment',
          'RequestSubmitted',
        ].includes(notificationType))

    if (shouldRefreshList) {
      const refreshGeneration = ++requestListGeneration.current
      try {
        const requestData = await getRequests()
        if (
          notificationSession !== sessionGeneration.current ||
          refreshGeneration !== requestListGeneration.current
        ) return
        setRequests(requestData)
        setRequestsLoadFailed(false)
      } catch (caught) {
        if (
          notificationSession !== sessionGeneration.current ||
          refreshGeneration !== requestListGeneration.current
        ) return
        setRequestsLoadFailed(true)
        setError(`Request status could not be refreshed. ${messageFrom(caught)}`)
      }
    }

    if (selectedRequestId.current !== requestId) return

    const detailsGeneration = ++requestDetailsGeneration.current
    try {
      const [request, workflow] = await Promise.all([
        getRequest(requestId),
        getRequestWorkflow(requestId),
      ])
      if (
        notificationSession !== sessionGeneration.current ||
        detailsGeneration !== requestDetailsGeneration.current ||
        selectedRequestId.current !== requestId
      ) return
      setSelectedRequest(request)
      setSelectedWorkflow(workflow)
    } catch (caught) {
      if (
        notificationSession === sessionGeneration.current &&
        detailsGeneration === requestDetailsGeneration.current
      ) {
        setError(`Request details could not be refreshed. ${messageFrom(caught)}`)
      }
    }
  }, [profile?.role])

  const authenticate = useCallback(async (jwt: string) => {
    sessionGeneration.current += 1
    sessionStorage.setItem('flowdesk-token', jwt)
    setToasts([])
    setError('')
    setToken(jwt)
    navigateTo('Overview', true)
  }, [navigateTo])

  useEffect(() => {
    if (!token) return
    let active = true
    Promise.all([getMe()]).then(async ([currentUser]) => {
      if (!active) return
      setProfile(currentUser)
      navigateTo('Overview', true)
      await refresh(currentUser.role)
    }).catch((caught: unknown) => {
      if (!active) return
      if (caught instanceof ApiError && caught.status === 401) {
        clearSession()
        setError('Your session has expired. Please sign in again.')
      }
      else setError(messageFrom(caught))
    }).finally(() => {
      if (active) setLoading(false)
    })
    return () => { active = false }
  }, [token, clearSession, navigateTo, refresh])

  const pages = profile ? rolePages[roleName(profile.role)] : []
  const visiblePage = pages.includes(page) ? page : 'Overview'

  async function handleLogin(email: string, password: string) {
    setLoading(true)
    setError('')
    try {
      const result = await login(email, password)
      await authenticate(result.accessToken)
    } catch (caught) {
      setError(messageFrom(caught))
      throw caught
    } finally {
      setLoading(false)
    }
  }

  async function openRequest(item: RequestItem) {
    setError('')
    selectedRequestId.current = item.id
    const detailsGeneration = ++requestDetailsGeneration.current
    try {
      const [request, workflow] = await Promise.all([
        getRequest(item.id),
        getRequestWorkflow(item.id),
      ])
      if (
        detailsGeneration !== requestDetailsGeneration.current ||
        selectedRequestId.current !== item.id
      ) return
      setSelectedRequest(request)
      setSelectedWorkflow(workflow)
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 401) {
        clearSession()
        setError('Your session has expired. Please sign in again.')
      }
      else if (caught instanceof ApiError && caught.status === 403) setError('You do not have permission to view this request.')
      else setError(messageFrom(caught))
    }
  }

  async function openNotificationRequest(requestId: number) {
    const existing = requests.find((item) => item.id === requestId)
    if (existing) {
      await openRequest(existing)
      return
    }

    selectedRequestId.current = requestId
    const detailsGeneration = ++requestDetailsGeneration.current
    try {
      const [request, workflow] = await Promise.all([
        getRequest(requestId),
        getRequestWorkflow(requestId),
      ])
      if (
        detailsGeneration !== requestDetailsGeneration.current ||
        selectedRequestId.current !== requestId
      ) return
      setSelectedRequest(request)
      setSelectedWorkflow(workflow)
    } catch (caught) {
      setError(messageFrom(caught))
    }
  }

  function signOut() {
    clearSession()
    navigateTo('Overview', true)
    setMobileMenuOpen(false)
    setError('')
  }

  async function runAction(action: () => Promise<void>, success: string) {
    const actionSession = sessionGeneration.current
    setError('')
    try {
      await action()
    } catch (caught) {
      if (actionSession !== sessionGeneration.current) return false
      if (caught instanceof ApiError && caught.status === 401) {
        clearSession()
        setError('Your session has expired. Please sign in again.')
      }
      else if (caught instanceof ApiError && caught.status === 403) setError('You do not have permission to perform this action.')
      else setError(messageFrom(caught))
      if (actionSession === sessionGeneration.current) {
        showToast('error', messageFrom(caught))
      }
      return false
    }
    if (actionSession !== sessionGeneration.current) return false
    showToast('success', success)
    if (profile) {
      try {
        await refresh(profile.role)
      } catch (caught) {
        if (caught instanceof ApiError && caught.status === 401) {
          clearSession()
          setError('Your session has expired. Please sign in again.')
        } else {
          setError(`The change was saved, but the latest workspace data could not be loaded. ${messageFrom(caught)}`)
        }
      }
    }
    return true
  }

  async function decideRequest(
    id: number,
    action: 'approve' | 'reject' | 'cancel',
    comment?: string,
  ) {
    if (pendingActionId !== null) return false
    const actionSession = sessionGeneration.current
    setPendingActionId(id)
    setError('')
    try {
      await patchRequest(id, action, comment)
      if (actionSession !== sessionGeneration.current) return false
      if (profile) {
        try {
          await refresh(profile.role)
          const [updatedRequest, updatedWorkflow] = await Promise.all([
            getRequest(id),
            getRequestWorkflow(id),
          ])
          setSelectedRequest((current) => current?.id === id ? updatedRequest : current)
          setSelectedWorkflow((current) => selectedRequest?.id === id ? updatedWorkflow : current)
          const message = action === 'approve' && updatedRequest.status === 'Pending'
            ? 'Stage approved. The next workflow stage is active.'
            : action === 'reject'
              ? 'Request rejected with your reason recorded.'
              : action === 'cancel'
                ? 'Request cancelled successfully.'
                : 'Request approved successfully.'
          showToast('success', message)
        } catch (caught) {
          if (actionSession !== sessionGeneration.current) return false
          setError(`Decision saved, but the latest request list could not be loaded. ${messageFrom(caught)}`)
          showToast('warning', `Decision saved, but the latest request list could not be loaded. ${messageFrom(caught)}`)
        }
      } else {
        showToast('success', 'Decision recorded successfully.')
      }
      return true
    } catch (caught) {
      if (actionSession !== sessionGeneration.current) return false
      if (caught instanceof ApiError && caught.status === 401) {
        clearSession()
        setError('Your session has expired. Please sign in again.')
      } else if (caught instanceof ApiError && caught.status === 403) {
        setError('You do not have permission to perform this action.')
      } else {
        setError(messageFrom(caught))
      }
      if (actionSession === sessionGeneration.current) {
        showToast('error', messageFrom(caught))
      }
      return false
    } finally {
      setPendingActionId(null)
    }
  }

  const counts = useMemo(() => ({
    total: requests.length,
    pending: requests.filter((item) => item.status.toLowerCase() === 'pending').length,
    approved: requests.filter((item) => item.status.toLowerCase() === 'approved').length,
    rejected: requests.filter((item) => item.status.toLowerCase() === 'rejected').length,
    cancelled: requests.filter((item) => item.status.toLowerCase() === 'cancelled').length,
  }), [requests])

  if (!profile) {
    if (token && loading) {
      return <main className="session-loading" aria-label="Loading workspace">
        <div className="skeleton-sidebar"><span /><span /><span /><span /><span /></div>
        <div className="skeleton-workspace"><div className="skeleton-topbar" /><div className="skeleton-heading" /><div className="skeleton-stats"><span /><span /><span /><span /></div><div className="skeleton-content" /></div>
      </main>
    }
    return (
      <LoginScreen
        loading={loading}
        error={error}
        onLogin={handleLogin}
        onDismissError={() => setError('')}
      />
    )
  }

  return (
    <div className={`app-shell ${sidebarCollapsed ? 'sidebar-collapsed' : ''} ${mobileMenuOpen ? 'mobile-menu-open' : ''}`}>
      {mobileMenuOpen && <button type="button" className="mobile-nav-scrim" aria-label="Close navigation" onClick={() => setMobileMenuOpen(false)} />}
      <aside
        className="sidebar"
        id="primary-navigation-drawer"
        ref={sidebarRef}
        role={mobileMenuOpen ? 'dialog' : undefined}
        aria-label={mobileMenuOpen ? 'Navigation menu' : undefined}
        aria-modal={mobileMenuOpen || undefined}
      >
        <a className="brand" href={pageHash('Overview')} aria-label="FlowDesk home" onClick={(event) => { event.preventDefault(); navigateTo('Overview'); setMobileMenuOpen(false) }}>
          <span className="brand-mark"><span>F</span></span>
          <span className="brand-name">FlowDesk</span>
        </a>
        <div className="sidebar-workspace"><span className="workspace-dot" /> Approval workspace</div>
        <nav className="side-nav" aria-label="Main navigation">
          {pages.map((item) => (
            <button className={`nav-link ${visiblePage === item ? 'active' : ''}`} key={item} title={navLabel(item)} aria-label={navLabel(item)} aria-current={visiblePage === item ? 'page' : undefined} onClick={() => { navigateTo(item); setError(''); setMobileMenuOpen(false) }}>
              <NavIcon page={item} />
              <span>{navLabel(item)}</span>
              {item === 'Assigned to Me' && counts.pending > 0 && <span className="nav-count">{counts.pending}</span>}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <button className="nav-link logout-nav-button" onClick={signOut}><svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M10 17l5-5-5-5m5 5H3m10-9h6a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-6" /></svg><span>Sign out</span></button>
          <button className="sidebar-profile" onClick={() => { navigateTo('Profile'); setMobileMenuOpen(false) }} aria-label={`Open profile for ${profile.fullName}`}>
            <Avatar
              name={profile.fullName}
              imageUrl={profile.hasProfileImage ? profile.profileImageUrl : null}
            />
            <span className="sidebar-profile-copy"><strong>{profile.fullName}</strong><small>{roleName(profile.role)}</small></span>
            <span className="sidebar-profile-more" aria-hidden="true">···</span>
          </button>
        </div>
      </aside>

      <main className="main-panel" inert={mobileMenuOpen}>
        <header className="topbar">
          <div className="topbar-left">
            <button ref={sidebarToggleButtonRef} className="sidebar-toggle" aria-label={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'} onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}><span /><span /><span /></button>
            <button
              ref={mobileMenuButtonRef}
              className="mobile-menu-toggle"
              type="button"
              aria-label={mobileMenuOpen ? 'Close navigation' : 'Open navigation'}
              aria-controls="primary-navigation-drawer"
              aria-expanded={mobileMenuOpen}
              onClick={() => setMobileMenuOpen((open) => !open)}
            ><span /><span /><span /></button>
            <div className="breadcrumbs"><span>Workspace</span><span className="crumb-separator">/</span><strong>{navLabel(visiblePage)}</strong></div>
          </div>
          <div className="topbar-actions">
            <span className="topbar-role">{roleName(profile.role)} workspace</span>
            <WorkflowNotifications
              onError={setError}
              onNavigateToRequest={openNotificationRequest}
              onRequestNotification={refreshAfterRequestNotification}
              onIncomingNotification={handleIncomingNotification}
              unreadCount={notificationUnreadCount}
              onUnreadCountChange={setNotificationUnreadCount}
            />
            <button className="header-profile-button" aria-label={`Profile for ${profile.fullName}`} onClick={() => navigateTo('Profile')}><Avatar name={profile.fullName} imageUrl={profile.hasProfileImage ? profile.profileImageUrl : null} /></button>
          </div>
        </header>

        <DashboardAnalyticsProvider>
        <div className="page-content">
          {isRequestPage(visiblePage) && <div className="request-page-tabs">
            {requestPagesForRole(roleName(profile.role)).map((requestPage) => <button className={visiblePage === requestPage.page ? 'selected' : ''} key={requestPage.page} onClick={() => navigateTo(requestPage.page)}>{requestPage.label}</button>)}
          </div>}
          {error && <div className="alert alert-error" role="alert"><span>{error}</span><button onClick={() => setError('')} aria-label="Dismiss error">×</button></div>}
          <ToastViewport toasts={toasts} onDismiss={dismissToast} />
          {loading && <div className="loading-strip"><span className="spinner" /> Loading your workspace…</div>}
          {visiblePage === 'Overview' && <Overview profile={profile} requests={requests} requestsLoadFailed={requestsLoadFailed} counts={counts} onOpenRequest={(item) => { void openRequest(item) }} onNavigate={navigateTo} analytics={<Suspense fallback={<div className="analytics-chunk-loading" role="status">Loading request activity…</div>}><DashboardAnalytics mode="overview" /></Suspense>} />}
          {visiblePage === 'Analytics' && <Suspense fallback={<div className="analytics-chunk-loading" role="status">Loading analytics…</div>}><DashboardAnalytics mode="full" /></Suspense>}
          {visiblePage === 'Reports' && <Reports role={roleName(profile.role)} />}
          {visiblePage === 'My Leave' && <LeaveManagement role="Employee" onOpenRequest={() => navigateTo('My Requests')} />}
          {visiblePage === 'Team Leave' && <LeaveManagement role="Manager" onOpenRequest={() => navigateTo('Assigned to Me')} />}
          {visiblePage === 'Leave Management' && <LeaveManagement role="Admin" users={users} departments={departments} onOpenRequest={() => navigateTo('All Requests')} />}
          {(visiblePage === 'My Requests' || visiblePage === 'Assigned to Me' || visiblePage === 'Request History' || visiblePage === 'All Requests' || visiblePage === 'Assignment Management') && (
            <RequestsPage
              page={visiblePage}
              role={roleName(profile.role)}
              requests={requests}
              requestsLoadFailed={requestsLoadFailed}
              departments={departments}
              onOpen={(item) => { void openRequest(item) }}
              onAction={decideRequest}
              onAssign={(request) => setAssignmentTarget(request)}
              busyRequestId={pendingActionId}
              onCreate={() => navigateTo('New Request')}
            />
          )}
          {visiblePage === 'New Request' && <NewRequest
            requestTypes={requestTypes}
            onSubmit={(input, files, onProgress, clientUploadIds) => runAction(async () => {
              if (files.length > 0) {
                await createRequestWithAttachments(
                  input,
                  files,
                  onProgress,
                  clientUploadIds,
                )
              } else {
                await createTypedRequest(input)
              }
              navigateTo('My Requests')
            }, 'Your request has been submitted.')}
          />}
          {visiblePage === 'Workflow Management' && <WorkflowManagement departments={departments} users={users} />}
          {visiblePage === 'Audit Logs' && <AuditLogs />}
          {visiblePage === 'Request Types' && <RequestTypeManagement />}
          {visiblePage === 'User Management' && <UserManagement
            users={users}
            departments={departments}
            actionError={error}
            onCreateUser={(input) => runAction(async () => { await createAdminUser(input) }, 'User created successfully.')}
            onUpdateUser={(userId, role, departmentId) => runAction(async () => {
              const existing = users.find((user) => user.id === userId)
              if (!existing) throw new Error('The selected user could not be found.')
              if (role !== existing.role) await updateUserRole(userId, role)
              if (departmentId !== existing.departmentId) await updateUserDepartment(userId, departmentId)
            }, 'User details updated.')}
            onActiveChange={(userId, isActive) => runAction(async () => { await updateUserActive(userId, isActive) }, `User ${isActive ? 'activated' : 'deactivated'}.`)}
            onViewUser={async (userId) => {
              try {
                setSelectedUser(await getUser(userId))
              } catch (caught) {
                setError(messageFrom(caught))
              }
            }}
          />}
          {visiblePage === 'Departments' && <DepartmentsPage users={users} departments={departments} actionError={error} onCreate={(name, code) => runAction(async () => { await createDepartment(name, code) }, 'Department created.')} />}
          {visiblePage === 'Profile' && <ProfilePage
            profile={profile}
            onUploadPhoto={async (image) => {
              const updated = await uploadMyProfilePhoto(image)
              setProfile(updated)
              showToast('success', 'Profile photo updated.')
            }}
            onRemovePhoto={async () => {
              await removeMyProfilePhoto()
              setProfile((current) => current
                ? { ...current, hasProfileImage: false }
                : current)
              showToast('success', 'Profile photo removed.')
            }}
          />}
        </div>
        </DashboardAnalyticsProvider>
        <footer className="page-footer"><span>FlowDesk Approval Management</span><span>Secure approvals, clearly managed.</span></footer>
      </main>

      {selectedRequest && <RequestDetails
        item={selectedRequest}
        workflow={selectedWorkflow}
        role={roleName(profile.role)}
        hasCancellationRequest={requests.some((request) =>
          request.requestDetails?.cancellationOfRequestId === selectedRequest.id)}
        busy={pendingActionId === selectedRequest.id}
        actionError={error}
        departments={departments}
        users={users}
        onClose={() => { selectedRequestId.current = null; requestDetailsGeneration.current += 1; setSelectedRequest(null); setSelectedWorkflow(null) }}
        onAction={(action, comment) => decideRequest(selectedRequest.id, action, comment)}
        onRequestCancellation={(reason) => runAction(async () => {
          await createLeaveCancellationRequest(selectedRequest.id, reason)
        }, 'Cancellation request submitted for approval.')}
        onAssign={async (managerId) => {
          const succeeded = await runAction(
            async () => { await assignRequest(selectedRequest.id, managerId) },
            'Manager assigned successfully.',
          )
          if (succeeded) {
            try {
              setSelectedRequest(await getRequest(selectedRequest.id))
              setSelectedWorkflow(await getRequestWorkflow(selectedRequest.id))
            } catch (caught) {
              setError(`Manager assigned, but request details could not be refreshed. ${messageFrom(caught)}`)
            }
          }
          return succeeded
        }}
      />}
      {assignmentTarget && <ManagerAssignmentModal
        request={assignmentTarget}
        managers={users.filter((user) => user.role.toLowerCase() === 'manager')}
        departments={departments}
        actionError={error}
        onClose={() => setAssignmentTarget(null)}
        onAssign={async (requestId, managerId) => {
          const succeeded = await runAction(
            async () => { await assignRequest(requestId, managerId) },
            'Manager assigned successfully.',
          )
          if (succeeded) setAssignmentTarget(null)
          return succeeded
        }}
      />}
      {selectedUser && <UserDetailsModal user={selectedUser} departments={departments} onClose={() => setSelectedUser(null)} />}
    </div>
  )
}

function isRequestPage(page: Page) {
  return ['My Requests', 'Assigned to Me', 'Request History', 'All Requests', 'Assignment Management'].includes(page)
}

function requestPagesForRole(role: string): { page: Page; label: string }[] {
  if (role === 'Manager') return [{ page: 'Assigned to Me', label: 'Assigned to Me' }, { page: 'Request History', label: 'Request History' }]
  if (role === 'Admin') return [{ page: 'All Requests', label: 'All Requests' }, { page: 'Assignment Management', label: 'Assignment Management' }, { page: 'User Management', label: 'Users' }, { page: 'Departments', label: 'Departments' }]
  return [{ page: 'My Requests', label: 'My Requests' }, { page: 'New Request', label: 'New Request' }]
}

function LoginScreen({ loading, error, onLogin, onDismissError }: { loading: boolean; error: string; onLogin: (email: string, password: string) => Promise<void>; onDismissError: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    try { await onLogin(email.trim(), password) } catch { /* Error is displayed by the parent. */ } finally { setSubmitting(false) }
  }
  return (
    <main className="login-shell">
      <section className="login-story">
        <div className="login-brand"><span className="brand-mark"><span>F</span></span><span>FlowDesk</span></div>
        <div className="login-story-content">
          <div className="login-story-kicker"><span /> Approval operations, made clear</div>
          <h2>Move every decision<br />forward with confidence.</h2>
          <p>One thoughtful workspace for requests, reviews, and the people behind every approval.</p>
          <div className="login-visual" aria-hidden="true"><span className="visual-orbit orbit-one" /><span className="visual-orbit orbit-two" /><span className="visual-card visual-card-back" /><span className="visual-card visual-card-front"><i /><i /><i /></span><span className="visual-spark spark-one" /><span className="visual-spark spark-two" /></div>
        </div>
        <div className="login-story-footer">FLOWDESK <span>·</span> APPROVAL MANAGEMENT</div>
      </section>
      <section className="login-form-side">
        <div className="login-card">
          <div className="login-kicker"><span className="kicker-line" /> APPROVALS WORKSPACE</div>
          <h1>Welcome back</h1>
          <p className="login-description">Sign in to manage your approvals and requests.</p>
          {error && <div className="alert alert-error" role="alert"><span>{error}</span><button onClick={onDismissError} aria-label="Dismiss error">×</button></div>}
          <form className="form-stack" onSubmit={submit}>
            <label>Email address<input type="email" autoComplete="username" placeholder="you@company.com" value={email} onChange={(event) => setEmail(event.target.value)} required /></label>
            <label>Password<span className="password-input-wrap"><input aria-label="Password" type={showPassword ? 'text' : 'password'} autoComplete="current-password" placeholder="Enter your password" value={password} onChange={(event) => setPassword(event.target.value)} required /><button type="button" className="password-toggle" aria-label={showPassword ? 'Hide password' : 'Show password'} onClick={() => setShowPassword((shown) => !shown)}>{showPassword ? 'Hide' : 'Show'}</button></span></label>
            <button className="button button-primary login-submit" disabled={submitting || loading}>{submitting || loading ? <><span className="spinner" />Signing in…</> : <>Sign in<span>→</span></>}</button>
          </form>
          <div className="login-note"><span className="lock-icon">✓</span> Your workspace is protected with secure sign-in.</div>
        </div>
      </section>
    </main>
  )
}

function Overview({ profile, requests, requestsLoadFailed, counts, onOpenRequest, onNavigate, analytics }: { profile: UserProfile; requests: RequestItem[]; requestsLoadFailed: boolean; counts: { total: number; pending: number; approved: number; rejected: number; cancelled: number }; onOpenRequest: (item: RequestItem) => void; onNavigate: (page: Page) => void; analytics: ReactNode }) {
  const role = roleName(profile.role)
  const [today] = useState(() => new Date())
  const greeting = today.getHours() < 12 ? 'Good morning' : today.getHours() < 18 ? 'Good afternoon' : 'Good evening'
  const cards = role === 'Employee'
    ? [{ label: 'Total Requests', value: counts.total, tone: 'blue', icon: '↗', context: 'Across your requests' }, { label: 'Pending Approvals', value: counts.pending, tone: 'amber', icon: '◷', context: 'Awaiting a decision' }, { label: 'Approved Requests', value: counts.approved, tone: 'green', icon: '✓', context: 'Successfully approved' }, { label: 'Rejected Requests', value: counts.rejected, tone: 'red', icon: '×', context: 'Need your attention' }]
    : role === 'Manager'
      ? [{ label: 'Pending Approvals', value: counts.pending, tone: 'amber', icon: '◷', context: 'Ready for your review' }, { label: 'Approved Requests', value: counts.approved, tone: 'green', icon: '✓', context: 'Reviewed by you' }, { label: 'Rejected Requests', value: counts.rejected, tone: 'red', icon: '×', context: 'Reviewed by you' }, { label: 'Total Reviewed', value: counts.approved + counts.rejected, tone: 'blue', icon: '↗', context: 'Completed decisions' }]
      : [
          { label: 'Total Requests', value: counts.total, tone: 'blue', icon: '↗', context: 'Across all departments' },
          { label: 'Pending Approvals', value: counts.pending, tone: 'amber', icon: '◷', context: 'Awaiting review' },
          { label: 'Approved Requests', value: counts.approved, tone: 'green', icon: '✓', context: 'All-time total' },
          { label: 'Rejected Requests', value: counts.rejected, tone: 'red', icon: '×', context: 'All-time total' },
        ]
  const allRecentRequests = [...requests].sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime())
  return (
    <>
      <div className="welcome-row dashboard-welcome"><div><div className="eyebrow">OVERVIEW <span className="eyebrow-dot">·</span> {role.toUpperCase()}</div><h1>{greeting}, {profile.fullName.split(' ')[0]}</h1><p className="page-subtitle">Here's what's happening with your approval workflow.</p></div>
        <div className="dashboard-actions"><time className="today-pill">{new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' }).format(today)}</time>
          {role === 'Employee' && <button className="button button-primary" onClick={() => onNavigate('New Request')}><span className="button-plus">+</span> New request</button>}
          {role === 'Manager' && <button className="button button-primary" onClick={() => onNavigate('Assigned to Me')}>Review requests <span>→</span></button>}
          {role === 'Admin' && <button className="button button-primary" onClick={() => onNavigate('User Management')}>Manage workspace <span>→</span></button>}
          <button className="button button-secondary dashboard-analytics-link" onClick={() => onNavigate('Analytics')}>View all analytics <span aria-hidden="true">→</span></button>
        </div>
      </div>
      <section className="stat-grid" aria-label="Summary statistics">
        {cards.map((card) => <article className="stat-card" key={card.label}><div className="stat-top"><span className={`stat-icon ${card.tone}`}>{card.icon}</span><span className="stat-context">{card.context}</span></div><div className="stat-value">{card.value}</div><div className="stat-label">{card.label}</div></article>)}
      </section>
      {analytics}
      <section className="content-card recent-card">
        <DashboardRecentRequests
          requests={allRecentRequests}
          role={role}
          loadFailed={requestsLoadFailed && requests.length === 0}
          onOpen={onOpenRequest}
          onViewAll={() => onNavigate(role === 'Manager' ? 'Assigned to Me' : role === 'Admin' ? 'All Requests' : 'My Requests')}
        />
      </section>
    </>
  )
}

function DashboardRecentRequests({ requests, role, loadFailed, onOpen, onViewAll }: {
  requests: RequestItem[]
  role: string
  loadFailed: boolean
  onOpen: (item: RequestItem) => void
  onViewAll: () => void
}) {
  const [statusFilter, setStatusFilter] = useState('All')
  const [query, setQuery] = useState('')
  const [submittedDate, setSubmittedDate] = useState('')
  const statusFilters = ['All', 'Pending', 'Approved', 'Rejected', 'Cancelled']
  const matchingRequests = requests.filter((request) => {
    const normalizedQuery = query.trim().toLowerCase()
    const requestId = `req-${String(request.id).padStart(4, '0')}`
    const matchesQuery = !normalizedQuery
      || requestId.includes(normalizedQuery)
      || request.title.toLowerCase().includes(normalizedQuery)
      || (request.departmentName || request.department || '').toLowerCase().includes(normalizedQuery)
    const matchesStatus = statusFilter === 'All' || request.status.toLowerCase() === statusFilter.toLowerCase()
    const matchesDate = !submittedDate || request.createdAtUtc.slice(0, 10) === submittedDate
    return matchesQuery && matchesStatus && matchesDate
  })
  const visibleRequests = matchingRequests.slice(0, 5)
  const title = role === 'Manager' ? 'Recently assigned' : 'Recent requests'
  const requesterHeading = role === 'Manager' ? 'SUBMITTED BY' : 'REQUESTED BY'
  const formatSubmittedTime = (value: string) => new Intl.DateTimeFormat(undefined, {
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(value))

  return <div className="dashboard-recent">
    <div className="dashboard-recent-heading">
      <div className="dashboard-recent-title">
        <span className="dashboard-recent-icon" aria-hidden="true">↗</span>
        <div><div className="section-eyebrow">{role === 'Manager' ? 'YOUR QUEUE' : role === 'Admin' ? 'ACTIVITY' : 'YOUR WORK'}</div><h2>{title}</h2><p>Track and manage your latest submissions.</p></div>
      </div>
      <button className="dashboard-recent-view-all" onClick={onViewAll}>View all requests <span aria-hidden="true">→</span></button>
    </div>
    <div className="dashboard-recent-toolbar">
      <div className="dashboard-recent-statuses" role="group" aria-label="Filter recent requests by status">
        {statusFilters.map((status) => {
          const count = status === 'All'
            ? requests.length
            : requests.filter((request) => request.status.toLowerCase() === status.toLowerCase()).length
          return <button
            key={status}
            className={`dashboard-recent-status${statusFilter === status ? ' selected' : ''}`}
            type="button"
            aria-pressed={statusFilter === status}
            onClick={() => setStatusFilter(status)}
          >
            <span>{status === 'All' ? 'All requests' : status}</span><span className={`dashboard-recent-count count-${status.toLowerCase()}`}>{count}</span>
          </button>
        })}
      </div>
      <div className="dashboard-recent-filters">
        <label className="dashboard-recent-search">
          <span aria-hidden="true">⌕</span>
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search by request ID, title or department…" aria-label="Search recent requests" />
        </label>
        <label className="dashboard-recent-date" aria-label="Filter recent requests by date">
          <span aria-hidden="true">▦</span>
          <span className="dashboard-recent-date-label">{submittedDate || 'Date'}</span>
          <input type="date" aria-label="Submitted date" value={submittedDate} onChange={(event) => setSubmittedDate(event.target.value)} />
        </label>
      </div>
    </div>
    {visibleRequests.length === 0
      ? <div className="dashboard-recent-empty" role="status">
        <strong>{loadFailed ? 'Requests unavailable' : matchingRequests.length === 0 && requests.length ? 'No matching requests' : 'No recent requests yet'}</strong>
        <p>{loadFailed ? 'Requests could not be loaded. Check the error above and try again.' : 'Try another search or status filter, or check back when requests are available.'}</p>
      </div>
      : <>
        <div className="dashboard-recent-table-wrap">
          <table className="dashboard-recent-table">
            <thead><tr><th aria-label="Row number">#</th><th>REQUEST</th><th>REQUEST TITLE</th><th>DEPARTMENT</th><th>AMOUNT</th><th>{requesterHeading}</th><th>SUBMITTED</th><th>STATUS</th><th>ACTION</th></tr></thead>
            <tbody>{visibleRequests.map((request, index) => <tr key={request.id}>
              <td className="dashboard-recent-index">{index + 1}</td>
              <td><div className="dashboard-request-cell"><span className={`dashboard-request-type type-${(request.requestTypeCode ?? 'request').toLowerCase()}`}>{requestTypeMark(request.requestTypeCode)}</span><span className="dashboard-request-id">REQ-{String(request.id).padStart(4, '0')}<small>{request.requestTypeName ?? 'Request'}</small></span></div></td>
              <td className="dashboard-request-title" title={request.title}>{request.title}</td>
              <td className="dashboard-recent-department">{request.departmentName || request.department || '—'}</td>
              <td className="dashboard-recent-amount">{money(request.amount)}</td>
              <td className="dashboard-recent-requester">{request.createdByName || request.submittedBy || 'Legacy request'}</td>
              <td className="dashboard-recent-date-value">{formatDate(request.createdAtUtc)}<small>{formatSubmittedTime(request.createdAtUtc)}</small></td>
              <td><StatusBadge status={request.status} /></td>
              <td><button className="dashboard-recent-open" type="button" onClick={() => onOpen(request)}>View details <span aria-hidden="true">→</span></button></td>
            </tr>)}</tbody>
          </table>
        </div>
        <div className="dashboard-recent-mobile-list">{visibleRequests.map((request) => <article className="dashboard-recent-mobile-item" key={request.id}>
          <div className="dashboard-recent-mobile-top"><span className="dashboard-request-id">REQ-{String(request.id).padStart(4, '0')}<small>{request.requestTypeName ?? 'Request'}</small></span><StatusBadge status={request.status} /></div>
          <strong className="dashboard-request-title">{request.title}</strong>
          <div className="dashboard-recent-mobile-meta"><span>{request.departmentName || request.department || 'No department'}</span><span>{money(request.amount)}</span></div>
          <div className="dashboard-recent-mobile-footer"><span>{formatDate(request.createdAtUtc)}</span><button className="dashboard-recent-open" type="button" onClick={() => onOpen(request)}>View details <span aria-hidden="true">→</span></button></div>
        </article>)}</div>
        <div className="dashboard-recent-footer"><span>Showing 1–{visibleRequests.length} of {matchingRequests.length} requests</span>{matchingRequests.length > 5 && <span>Latest requests first</span>}</div>
      </>}
  </div>
}

function requestTypeMark(code?: string | null) {
  switch (code) {
    case 'MATERIAL_PURCHASE': return 'MP'
    case 'LEAVE': return 'LV'
    case 'EXPENSE_REIMBURSEMENT': return 'ER'
    case 'IT_ACCESS': return 'IT'
    case 'TRAVEL': return 'TR'
    case 'WORK_FROM_HOME': return 'WF'
    default: return 'RQ'
  }
}

function RequestsPage({ page, role, requests, requestsLoadFailed, departments, onOpen, onAction, onAssign, busyRequestId, onCreate }: {
  page: Page
  role: string
  requests: RequestItem[]
  requestsLoadFailed: boolean
  departments: Department[]
  onOpen: (item: RequestItem) => void
  onAction: (id: number, action: 'approve' | 'reject' | 'cancel') => Promise<boolean>
  onAssign: (request: RequestItem) => void
  busyRequestId: number | null
  onCreate: () => void
}) {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('All statuses')
  const [departmentId, setDepartmentId] = useState('All departments')
  const [currentPage, setCurrentPage] = useState(1)
  const pageSize = 8
  const pageRequests = role === 'Admin' && page === 'Assignment Management'
    ? requests.filter((item) => item.status.toLowerCase() === 'pending')
    : role === 'Manager' && page === 'Assigned to Me'
    ? requests.filter((item) => item.status.toLowerCase() === 'pending')
    : role === 'Manager' && page === 'Request History'
      ? requests.filter((item) => item.status.toLowerCase() !== 'pending')
      : requests
  const activeStatus = role === 'Manager' && page === 'Assigned to Me'
    ? 'Pending'
    : role === 'Manager' && page === 'Request History' && status === 'Pending'
      ? 'All statuses'
      : status
  const filtered = pageRequests.filter((item) => {
    const normalizedQuery = query.trim().toLowerCase()
    const matchesQuery = item.title.toLowerCase().includes(normalizedQuery)
      || item.submittedBy?.toLowerCase().includes(normalizedQuery)
      || item.department?.toLowerCase().includes(normalizedQuery)
      || String(item.id).includes(normalizedQuery)
    const matchesDepartment = departmentId === 'All departments' || String(item.departmentId ?? '') === departmentId
    return matchesQuery && matchesDepartment && (activeStatus === 'All statuses' || item.status.toLowerCase() === activeStatus.toLowerCase())
  })
  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize))
  const visibleRequests = filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize)
  const setFilterStatus = (nextStatus: string) => {
    setCurrentPage(1)
    setStatus(nextStatus)
  }
  const tabs = role === 'Manager' && page === 'Assigned to Me'
    ? ['Pending']
    : role === 'Manager' && page === 'Request History'
      ? ['All history', 'Approved', 'Rejected', 'Cancelled']
      : role === 'Admin' && page === 'Assignment Management'
        ? ['Pending']
      : role === 'Manager'
        ? ['All assigned', 'Pending', 'Approved', 'Rejected', 'Cancelled']
        : ['All requests', 'Pending', 'Approved', 'Rejected', 'Cancelled']
  const statusOptions = [
    {
      value: 'All statuses',
      label: tabs[0].startsWith('All')
        ? tabs[0]
        : page === 'Assignment Management'
          ? 'All pending requests'
          : page === 'Assigned to Me'
            ? 'All assigned requests'
            : 'All requests',
      count: pageRequests.length,
    },
    ...tabs.filter((tab) => !tab.startsWith('All')).map((tab) => ({
      value: tab,
      label: tab,
      count: pageRequests.filter((item) => item.status.toLowerCase() === tab.toLowerCase()).length,
    })),
  ]
  return (
    <>
      <div className="welcome-row request-page-heading"><div><div className="eyebrow">WORKSPACE <span className="eyebrow-dot">·</span> {role.toUpperCase()}</div><h1>{page === 'Assignment Management' ? 'Assignment Management' : page}</h1><p className="page-subtitle">{page === 'Assignment Management' ? 'Route pending requests to an eligible Manager in the same department.' : page === 'All Requests' ? 'Monitor requests and assignment status across departments.' : 'Review request status and approval history.'}</p></div></div>
      <div className="request-workspace">
        <section className="content-card request-list-card">
          <div className={`request-search-row${role === 'Admin' ? ' admin-request-search-row' : ''}`}>
            <label className="search-field"><span className="search-symbol">Search</span><input value={query} onChange={(event) => { setQuery(event.target.value); setCurrentPage(1) }} placeholder="Search title, submitter, department…" aria-label="Search requests" /></label>
            <label className="request-status-filter">Status<select aria-label="Filter requests by status" value={activeStatus} onChange={(event) => setFilterStatus(event.target.value)}>{statusOptions.map((option) => <option key={option.value} value={option.value}>{option.label} ({option.count})</option>)}</select></label>
            {role === 'Admin' && <label className="request-department-filter">Department<select value={departmentId} onChange={(event) => { setDepartmentId(event.target.value); setCurrentPage(1) }}><option>All departments</option>{departments.map((department) => <option key={department.id} value={String(department.id)}>{department.name}</option>)}</select></label>}
            {role === 'Employee' && <button className="button button-primary request-create-button" onClick={onCreate}>New request</button>}
            <span className="result-count">{filtered.length} results</span>
          </div>
          {role === 'Manager' && <div className="inline-information request-page-note">Requests without a Manager assignment are routed to an Admin for assignment.</div>}
          {role === 'Admin' && page === 'Assignment Management' && <div className="inline-information request-page-note">Assign pending requests to an eligible Manager in the same department. Requests with an active assignment are marked as assigned.</div>}
          {errorMessageForEmpty(role, pageRequests) && <div className="inline-information">{errorMessageForEmpty(role, pageRequests)}</div>}
          <RequestTable requests={visibleRequests} onOpen={onOpen} role={role} onAction={onAction} onAssign={onAssign} busyRequestId={busyRequestId} loadFailed={requestsLoadFailed && requests.length === 0} assignmentManagement={role === 'Admin' && page === 'Assignment Management'} />
          {filtered.length > pageSize && <nav className="pagination" aria-label="Request list pages">
            <span>Showing {(currentPage - 1) * pageSize + 1}–{Math.min(currentPage * pageSize, filtered.length)} of {filtered.length}</span>
            <div><button onClick={() => setCurrentPage((value) => Math.max(1, value - 1))} disabled={currentPage === 1}>Previous</button><span>Page {currentPage} of {pageCount}</span><button onClick={() => setCurrentPage((value) => Math.min(pageCount, value + 1))} disabled={currentPage === pageCount}>Next</button></div>
          </nav>}
        </section>
      </div>
    </>
  )
}

function errorMessageForEmpty(role: string, requests: RequestItem[]) {
  return role === 'Manager' && requests.length === 0
    ? 'Only requests assigned to your account are shown here. Ask an Admin to assign unassigned requests.'
    : ''
}

function RequestTable({ requests, onOpen, compact = false, role, onAction, onAssign, busyRequestId = null, loadFailed = false, assignmentManagement = false }: {
  requests: RequestItem[]
  onOpen: (item: RequestItem) => void
  compact?: boolean
  role?: string
  onAction?: (id: number, action: 'approve' | 'reject' | 'cancel') => Promise<boolean>
  onAssign?: (request: RequestItem) => void
  busyRequestId?: number | null
  loadFailed?: boolean
  assignmentManagement?: boolean
}) {
  if (requests.length === 0) return <div className={`empty-state ${compact ? 'compact-empty' : ''}`}><span className="empty-icon">—</span><strong>{loadFailed ? 'Requests unavailable' : assignmentManagement ? 'No pending requests to assign' : role === 'Manager' ? 'No assigned requests' : 'No requests yet'}</strong><p>{loadFailed ? 'Requests could not be loaded. Check the error above and try again.' : assignmentManagement ? 'New pending requests will appear here when they need a Manager assignment.' : role === 'Manager' ? 'New items appear here after an Admin assigns them to you.' : 'When requests are available, they’ll appear here.'}</p></div>
  return (
    <>
      <div className="table-scroll request-table-scroll">
        <table className={`request-table ${role === 'Admin' ? 'admin-request-table' : ''} ${role === 'Manager' ? 'manager-request-table' : ''} ${assignmentManagement ? 'assignment-management-table' : ''}`}>
          <thead><tr><th>ID</th><th>REQUEST TITLE</th><th>SUBMITTED BY</th><th>DEPARTMENT</th>{role === 'Admin' && <th>ASSIGNED MANAGER</th>}<th>AMOUNT</th><th>SUBMITTED</th><th>STATUS</th><th>ACTION</th></tr></thead>
          <tbody>
            {requests.map((item) => {
              const hasPendingAssignment = item.assignments?.some((assignment) => assignment.status.toLowerCase() === 'pending') ?? false
              return <tr key={item.id} className="clickable-row">
                <td className="request-id">REQ-{String(item.id).padStart(4, '0')}</td>
                <td><RequestTitle item={item} /></td>
                <td className="request-cell-text">{item.createdByName || item.submittedBy || 'Legacy request'}</td>
                <td className="request-cell-text">{item.departmentName || item.department || '—'}</td>
                {role === 'Admin' && <td><AssignedManagers assignments={item.assignments ?? []} /></td>}
                <td className="amount-cell">{money(item.amount)}</td>
                <td>{formatDate(item.createdAtUtc)}</td>
                <td><StatusBadge status={item.status} /></td>
                <td className="row-actions">
                  <RequestRowActions item={item} role={role} hasPendingAssignment={hasPendingAssignment} onOpen={onOpen} onAction={onAction} onAssign={onAssign} busy={busyRequestId === item.id} />
                </td>
              </tr>
            })}
          </tbody>
        </table>
      </div>
      <div className="request-card-list">
        {requests.map((item) => {
          const hasPendingAssignment = item.assignments?.some((assignment) => assignment.status.toLowerCase() === 'pending') ?? false
          return <article className="request-mobile-card" key={item.id}>
            <div className="request-mobile-card-top">
              <span className="request-id">REQ-{String(item.id).padStart(4, '0')}</span>
              <StatusBadge status={item.status} />
            </div>
            <RequestTitle item={item} />
            <dl className="request-mobile-details">
              {item.priority && <div><dt>Priority</dt><dd><small className={`priority-label priority-${item.priority.toLowerCase()}`}>{item.priority}</small></dd></div>}
              <div><dt>Department</dt><dd>{item.departmentName || item.department || '—'}</dd></div>
              {role === 'Admin' && <div className="request-mobile-assignees"><dt>Assigned managers</dt><dd><AssignedManagers assignments={item.assignments ?? []} /></dd></div>}
              <div><dt>Submitted by</dt><dd>{item.createdByName || item.submittedBy || 'Legacy request'}</dd></div>
              <div><dt>Submitted</dt><dd>{formatDate(item.createdAtUtc)}</dd></div>
              <div><dt>Amount</dt><dd>{money(item.amount)}</dd></div>
            </dl>
            <RequestRowActions item={item} role={role} hasPendingAssignment={hasPendingAssignment} onOpen={onOpen} onAction={onAction} onAssign={onAssign} busy={busyRequestId === item.id} mobile />
          </article>
        })}
      </div>
    </>
  )
}

function RequestTitle({ item }: { item: RequestItem }) {
  return <div className="request-title request-title-copy">
    <strong title={item.title}>{item.title}</strong>
    <span className="request-title-badges">
      {item.requestTypeName && <small className="request-type-label">{item.requestTypeName}</small>}
      {item.priority && <small className={`priority-label priority-${item.priority.toLowerCase()}`}>{item.priority}</small>}
    </span>
  </div>
}

function AssignedManagers({ assignments }: { assignments: NonNullable<RequestItem['assignments']> }) {
  if (assignments.length === 0) return <span className="request-unassigned">Not assigned</span>
  const [first, ...additional] = assignments
  return <div className="assigned-managers">
    <span className="sr-only">Assignment records may include previous approvers.</span>
    <span className="assigned-manager-first" title={first.approverName}>
      <span>{first.approverName}</span><small>{first.status}</small>
    </span>
    {additional.length > 0 && <details className="assigned-managers-details">
      <summary role="button" tabIndex={0} aria-label={`${additional.length} more assigned ${additional.length === 1 ? 'manager' : 'managers'}`}>+{additional.length} more</summary>
      <div className="assigned-managers-popover">
        <strong>Assignment records</strong>
        <p>Status is shown for each assignment; entries may include prior approvers.</p>
        <ul>{assignments.map((assignment) => <li key={assignment.id}>
          <span title={assignment.approverName}>{assignment.approverName}</span>
          <small>{assignment.status}</small>
        </li>)}</ul>
      </div>
    </details>}
  </div>
}

function RequestRowActions({
  item,
  role,
  hasPendingAssignment,
  onOpen,
  onAction,
  onAssign,
  busy,
  mobile = false,
}: {
  item: RequestItem
  role?: string
  hasPendingAssignment: boolean
  onOpen: (item: RequestItem) => void
  onAction?: (id: number, action: 'approve' | 'reject' | 'cancel') => Promise<boolean>
  onAssign?: (request: RequestItem) => void
  busy: boolean
  mobile?: boolean
}) {
  const canDecide = role === 'Manager' && item.status.toLowerCase() === 'pending' && hasPendingAssignment
  const canCancel = role === 'Employee' && item.status.toLowerCase() === 'pending'
  return <div className={`row-button-group${mobile ? ' request-mobile-actions' : ''}`}>
    <button className={`row-action-button${canDecide ? ' review-action' : ''}`} onClick={() => onOpen(item)}>{mobile ? 'View details' : canDecide ? 'Review' : 'View'}</button>
    {canCancel && onAction && <button className="row-action-button reject-action" disabled={busy} onClick={() => { void onAction(item.id, 'cancel') }}>Cancel</button>}
    {role === 'Admin' && item.status.toLowerCase() === 'pending' && !hasPendingAssignment && onAssign && <button className="row-action-button review-action assign-manager-action" onClick={() => onAssign(item)}>Assign Manager</button>}
    {role === 'Admin' && item.status.toLowerCase() === 'pending' && hasPendingAssignment && <span className="assigned-label">Assigned</span>}
  </div>
}

function NewRequest({
  requestTypes,
  onSubmit,
}: {
  requestTypes: RequestType[]
  onSubmit: (
    input: CreateTypedRequestInput,
    files: File[],
    onProgress: (progress: number) => void,
    clientUploadIds: string[],
  ) => Promise<boolean>
}) {
  const [selectedRequestTypeCode, setSelectedRequestTypeCode] = useState(
    requestTypes[0]?.code ?? '',
  )
  const [itemName, setItemName] = useState('')
  const [quantity, setQuantity] = useState('')
  const [categoryOrLeaveType, setCategoryOrLeaveType] = useState('')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [expenseDate, setExpenseDate] = useState('')
  const [systemApplication, setSystemApplication] = useState('')
  const [requestedAccessLevel, setRequestedAccessLevel] = useState('')
  const [durationDays, setDurationDays] = useState('')
  const [destination, setDestination] = useState('')
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [justification, setJustification] = useState('')
  const [priority, setPriority] = useState<NonNullable<RequestItem['priority']>>('Medium')
  const [submitting, setSubmitting] = useState(false)
  const [attachments, setAttachments] = useState<File[]>([])
  const [uploadProgress, setUploadProgress] = useState(0)
  const attachmentUploadIds = useRef(new WeakMap<File, string>())
  const requestTypeCode = requestTypes.some((requestType) =>
    requestType.code === selectedRequestTypeCode)
    ? selectedRequestTypeCode
    : requestTypes[0]?.code ?? ''
  const selectedType = requestTypes.find(
    (requestType) => requestType.code === requestTypeCode,
  )

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selectedType) return
    setSubmitting(true)
    try {
      let input: CreateTypedRequestInput
      switch (selectedType.code) {
        case 'MATERIAL_PURCHASE':
          input = {
            requestTypeCode: selectedType.code,
            amount: Number(amount),
            priority,
            materialPurchase: {
              itemName: itemName.trim(),
              quantity: Number(quantity),
              businessJustification: justification.trim(),
            },
          }
          break
        case 'LEAVE':
          input = {
            requestTypeCode: selectedType.code,
            priority,
            leave: {
              leaveType: categoryOrLeaveType.trim(),
              startDate,
              endDate,
              reason: justification.trim(),
            },
          }
          break
        case 'EXPENSE_REIMBURSEMENT':
          input = {
            requestTypeCode: selectedType.code,
            amount: Number(amount),
            priority,
            expenseReimbursement: {
              category: categoryOrLeaveType.trim(),
              expenseDate,
              businessJustification: justification.trim(),
            },
          }
          break
        case 'IT_ACCESS':
          input = {
            requestTypeCode: selectedType.code,
            priority,
            itAccess: {
              systemApplication: systemApplication.trim(),
              requestedAccessLevel: requestedAccessLevel.trim(),
              durationDays: durationDays ? Number(durationDays) : null,
              businessJustification: justification.trim(),
            },
          }
          break
        case 'TRAVEL':
          input = {
            requestTypeCode: selectedType.code,
            amount: Number(amount),
            priority,
            travel: {
              destination: destination.trim(),
              startDate,
              endDate,
              businessPurpose: justification.trim(),
            },
          }
          break
        case 'WORK_FROM_HOME':
          input = {
            requestTypeCode: selectedType.code,
            priority,
            workFromHome: {
              startDate,
              endDate,
              reason: justification.trim(),
            },
          }
          break
        default:
          return
      }
      const clientUploadIds = attachments.map((file) => {
        let clientUploadId = attachmentUploadIds.current.get(file)
        if (!clientUploadId) {
          clientUploadId = crypto.randomUUID()
          attachmentUploadIds.current.set(file, clientUploadId)
        }
        return clientUploadId
      })
      await onSubmit({
        ...input,
        ...(description.trim() ? { description: description.trim() } : {}),
      }, attachments, setUploadProgress, clientUploadIds)
    } finally {
      setSubmitting(false)
    }
  }

  const needsJustification = Boolean(selectedType)
  return <>
    <div className="welcome-row"><div><div className="eyebrow">MY REQUESTS <span className="eyebrow-dot">·</span> NEW</div><h1>New request</h1><p className="page-subtitle">Select the request type and provide its required details.</p></div></div>
    <section className="content-card form-card">
      <div className="card-heading"><div><div className="section-eyebrow">REQUEST DETAILS</div><h2>What do you need?</h2></div><span className="required-note">* Required</span></div>
      {requestTypes.length === 0
        ? <div className="inline-information">No request types are currently enabled. Contact an Admin for assistance.</div>
        : <form className="form-stack request-form" onSubmit={(event) => { void submit(event) }}>
          <label htmlFor="request-type">Request type <span className="required-star" aria-hidden="true"></span><select id="request-type" value={requestTypeCode} onChange={(event) => setSelectedRequestTypeCode(event.target.value)} required>{requestTypes.map((type) => <option key={type.id} value={type.code}>{type.displayName}</option>)}</select></label>
          {selectedType?.code === 'MATERIAL_PURCHASE' && <>
            <label htmlFor="request-item-name">Item name <span className="required-star"></span><input id="request-item-name" value={itemName} onChange={(event) => setItemName(event.target.value)} maxLength={200} required /></label>
            <label htmlFor="request-quantity">Quantity <span className="required-star"></span><input id="request-quantity" type="number" min="1" step="1" value={quantity} onChange={(event) => setQuantity(event.target.value)} required /></label>
            <label htmlFor="request-amount">Estimated amount <span className="required-star"></span><input id="request-amount" type="number" min="0.01" step="0.01" value={amount} onChange={(event) => setAmount(event.target.value)} required /></label>
          </>}
          {selectedType?.code === 'LEAVE' && <>
            <label htmlFor="leave-type">Leave type <span className="required-star"></span><input id="leave-type" value={categoryOrLeaveType} onChange={(event) => setCategoryOrLeaveType(event.target.value)} maxLength={50} required /></label>
            <label htmlFor="leave-start-date">Start date <span className="required-star"></span><input id="leave-start-date" type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} required /></label>
            <label htmlFor="leave-end-date">End date <span className="required-star"></span><input id="leave-end-date" type="date" value={endDate} min={startDate || undefined} onChange={(event) => setEndDate(event.target.value)} required /></label>
            <p className="form-hint">Leave days are counted as inclusive calendar days. Holiday, working-day, and leave-balance rules are not implemented yet.</p>
          </>}
          {selectedType?.code === 'EXPENSE_REIMBURSEMENT' && <>
            <label htmlFor="expense-category">Expense category <span className="required-star"></span><input id="expense-category" value={categoryOrLeaveType} onChange={(event) => setCategoryOrLeaveType(event.target.value)} maxLength={100} required /></label>
            <label htmlFor="expense-date">Expense date <span className="required-star"></span><input id="expense-date" type="date" value={expenseDate} onChange={(event) => setExpenseDate(event.target.value)} required /></label>
            <label htmlFor="request-amount">Amount <span className="required-star"></span><input id="request-amount" type="number" min="0.01" step="0.01" value={amount} onChange={(event) => setAmount(event.target.value)} required /></label>
            <p className="form-hint">Receipt uploads are not available until secure attachment handling is implemented.</p>
          </>}
          {selectedType?.code === 'IT_ACCESS' && <>
            <label htmlFor="it-system">System or application <span className="required-star"></span><input id="it-system" value={systemApplication} onChange={(event) => setSystemApplication(event.target.value)} maxLength={150} required /></label>
            <label htmlFor="it-access-level">Requested access level <span className="required-star"></span><input id="it-access-level" value={requestedAccessLevel} onChange={(event) => setRequestedAccessLevel(event.target.value)} maxLength={100} required /></label>
            <label htmlFor="it-duration">Duration in days <span className="optional-note">(optional)</span><input id="it-duration" type="number" min="1" step="1" value={durationDays} onChange={(event) => setDurationDays(event.target.value)} /></label>
          </>}
          {selectedType?.code === 'TRAVEL' && <>
            <label htmlFor="travel-destination">Destination <span className="required-star"></span><input id="travel-destination" value={destination} onChange={(event) => setDestination(event.target.value)} maxLength={200} required /></label>
            <label htmlFor="travel-start-date">Start date <span className="required-star"></span><input id="travel-start-date" type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} required /></label>
            <label htmlFor="travel-end-date">End date <span className="required-star"></span><input id="travel-end-date" type="date" value={endDate} min={startDate || undefined} onChange={(event) => setEndDate(event.target.value)} required /></label>
            <label htmlFor="request-amount">Estimated budget <span className="required-star"></span><input id="request-amount" type="number" min="0.01" step="0.01" value={amount} onChange={(event) => setAmount(event.target.value)} required /></label>
          </>}
          {selectedType?.code === 'WORK_FROM_HOME' && <>
            <label htmlFor="wfh-start-date">Start date <span className="required-star"></span><input id="wfh-start-date" type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} required /></label>
            <label htmlFor="wfh-end-date">End date <span className="required-star"></span><input id="wfh-end-date" type="date" value={endDate} min={startDate || undefined} onChange={(event) => setEndDate(event.target.value)} required /></label>
          </>}
          <label className="form-field-full" htmlFor="request-description">Additional description <span className="optional-note">(optional, up to 2000 characters)</span><textarea id="request-description" value={description} onChange={(event) => setDescription(event.target.value)} maxLength={2000} rows={5} /></label>
          {needsJustification && <label className="form-field-full" htmlFor="request-justification">
            {selectedType?.code === 'LEAVE' || selectedType?.code === 'WORK_FROM_HOME'
              ? 'Reason'
              : selectedType?.code === 'TRAVEL'
                ? 'Business purpose'
                : 'Business justification'}
            {' '}<span className="required-star"></span>
            <textarea id="request-justification" value={justification} onChange={(event) => setJustification(event.target.value)} maxLength={2000} rows={4} required />
          </label>}
          <label htmlFor="request-priority">Priority<select id="request-priority" value={priority} onChange={(event) => setPriority(event.target.value as NonNullable<RequestItem['priority']>)}><option>Low</option><option>Medium</option><option>High</option><option>Urgent</option></select></label>
          <AttachmentPicker
            files={attachments}
            onChange={setAttachments}
            disabled={submitting}
          />
          {submitting && attachments.length > 0 && <div
            className="attachment-progress"
            role="status"
          >
            <span>Submitting request and attachments… {uploadProgress}%</span>
            <progress
              value={uploadProgress}
              max="100"
              aria-label="Upload progress"
            />
          </div>}
          <p className="form-hint">The server validates type-specific fields and routes using matching request-type, department, and workflow configuration. Missing matching routing requires Admin intervention.</p>
          <div className="form-actions"><button type="reset" className="button button-secondary" onClick={() => { setItemName(''); setQuantity(''); setCategoryOrLeaveType(''); setStartDate(''); setEndDate(''); setExpenseDate(''); setSystemApplication(''); setRequestedAccessLevel(''); setDurationDays(''); setDestination(''); setAmount(''); setJustification(''); setPriority('Medium'); setAttachments([]) }}>Clear</button><button className="button button-primary" disabled={submitting || !selectedType}>{submitting ? 'Submitting…' : 'Submit request'} <span>→</span></button></div>
        </form>}
    </section>
  </>
}

function UserManagement({ users, departments, actionError, onCreateUser, onUpdateUser, onActiveChange, onViewUser }: {
  users: User[]
  departments: Department[]
  actionError: string
  onCreateUser: (input: { fullName: string; email: string; password: string; role: 'Employee' | 'Manager'; departmentId: number }) => Promise<boolean>
  onUpdateUser: (id: number, role: string, departmentId: number | null) => Promise<boolean>
  onActiveChange: (id: number, isActive: boolean) => Promise<boolean>
  onViewUser: (id: number) => void
}) {
  const [showCreateForm, setShowCreateForm] = useState(false)
  const [userToUpdate, setUserToUpdate] = useState<User | null>(null)
  const [query, setQuery] = useState('')
  const [roleFilter, setRoleFilter] = useState('All roles')
  const [departmentFilter, setDepartmentFilter] = useState('All departments')
  const [currentPage, setCurrentPage] = useState(1)
  const pageSize = 10
  const filtered = users.filter((user) => {
    const search = query.trim().toLowerCase()
    const matchesSearch = !search
      || user.fullName.toLowerCase().includes(search)
      || user.email.toLowerCase().includes(search)
      || String(user.id).includes(search)
    return matchesSearch
      && (roleFilter === 'All roles' || user.role === roleFilter)
      && (departmentFilter === 'All departments' || String(user.departmentId ?? '') === departmentFilter)
  })
  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize))
  const visible = filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize)
  return <>
    <div className="welcome-row">
      <div><div className="eyebrow">WORKSPACE <span className="eyebrow-dot">·</span> ADMIN</div><h1>User Management</h1><p className="page-subtitle">Manage your organization's members and permissions.</p></div>
      <button className="button button-primary" onClick={() => setShowCreateForm(true)}><span className="button-plus">+</span> Add User</button>
    </div>
    <section className="content-card request-list-card">
      <div className="directory-toolbar">
        <div className="directory-title"><strong>Workspace members</strong><span>{filtered.length} of {users.length} accounts</span></div>
        <label className="search-field"><span className="search-symbol">Search</span><input value={query} onChange={(event) => { setQuery(event.target.value); setCurrentPage(1) }} placeholder="Name, email, or ID" aria-label="Search users" /></label>
        <label className="directory-filter">Role<select value={roleFilter} onChange={(event) => { setRoleFilter(event.target.value); setCurrentPage(1) }}><option>All roles</option><option>Employee</option><option>Manager</option><option>Admin</option></select></label>
        <label className="directory-filter">Department<select value={departmentFilter} onChange={(event) => { setDepartmentFilter(event.target.value); setCurrentPage(1) }}><option>All departments</option>{departments.map((department) => <option key={department.id} value={String(department.id)}>{department.name}</option>)}<option value="">No department</option></select></label>
      </div>
      {visible.length === 0 ? <EmptyState title={users.length ? 'No matching users' : 'No users found'} /> : <div className="table-scroll"><table className="request-table user-table"><thead><tr><th>ID</th><th>USER</th><th>ROLE</th><th>DEPARTMENT</th><th>STATUS</th><th>CREATED</th><th>ACTIONS</th></tr></thead><tbody>
        {visible.map((user) => <tr key={user.id}>
          <td>{user.id}</td>
          <td><div className="request-title"><Avatar name={user.fullName} imageUrl={user.hasProfileImage ? user.profileImageUrl : null} /><span><strong>{user.fullName}</strong><small>{user.email}</small></span></div></td>
          <td>{user.role}</td>
          <td>{departments.find((department) => department.id === user.departmentId)?.name ?? 'No department'}</td>
          <td><span className={`user-active-status ${user.isActive ? 'active' : 'inactive'}`}>{user.isActive ? 'Active' : 'Inactive'}</span></td>
          <td>{formatDate(user.createdAtUtc)}</td>
          <td className="row-actions"><div className="row-button-group user-row-actions"><button className="row-action-button" onClick={() => onViewUser(user.id)}>View</button><button className="row-action-button update-user-action" onClick={() => setUserToUpdate(user)}>Update</button><button className={`row-action-button ${user.isActive ? 'reject-action' : 'review-action'}`} onClick={() => { void onActiveChange(user.id, !user.isActive) }}>{user.isActive ? 'Deactivate' : 'Activate'}</button></div></td>
        </tr>)}
      </tbody></table></div>}
      {filtered.length > pageSize && <nav className="pagination" aria-label="User list pages"><span>Showing {(currentPage - 1) * pageSize + 1}–{Math.min(currentPage * pageSize, filtered.length)} of {filtered.length}</span><div><button onClick={() => setCurrentPage((value) => Math.max(1, value - 1))} disabled={currentPage === 1}>Previous</button><span>Page {currentPage} of {pageCount}</span><button onClick={() => setCurrentPage((value) => Math.min(pageCount, value + 1))} disabled={currentPage === pageCount}>Next</button></div></nav>}
    </section>
    {showCreateForm && <CreateUserModal
      departments={departments}
      actionError={actionError}
      onClose={() => setShowCreateForm(false)}
      onSubmit={async (input) => {
        const succeeded = await onCreateUser(input)
        if (succeeded) setShowCreateForm(false)
        return succeeded
      }}
    />}
    {userToUpdate && <UpdateUserModal
      user={userToUpdate}
      departments={departments}
      actionError={actionError}
      onClose={() => setUserToUpdate(null)}
      onSubmit={async (role, departmentId) => {
        const succeeded = await onUpdateUser(userToUpdate.id, role, departmentId)
        if (succeeded) setUserToUpdate(null)
        return succeeded
      }}
    />}
  </>
}

function UpdateUserModal({ user, departments, actionError, onClose, onSubmit }: {
  user: User
  departments: Department[]
  actionError: string
  onClose: () => void
  onSubmit: (role: string, departmentId: number | null) => Promise<boolean>
}) {
  const [role, setRole] = useState(user.role)
  const [departmentId, setDepartmentId] = useState(user.departmentId == null ? '' : String(user.departmentId))
  const [submitting, setSubmitting] = useState(false)
  const dialogRef = useRef<HTMLElement | null>(null)
  const hasChanges = role !== user.role || (departmentId ? Number(departmentId) : null) !== user.departmentId
  const departmentOptions = departments.filter((department) => department.isActive || department.id === user.departmentId)
  useDialogEscape(dialogRef, onClose, !submitting && !hasChanges)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!hasChanges) return
    setSubmitting(true)
    try {
      await onSubmit(role, departmentId ? Number(departmentId) : null)
    } finally {
      setSubmitting(false)
    }
  }

  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting && !hasChanges) onClose() }}>
    <section ref={dialogRef} className="detail-modal admin-form-modal" role="dialog" aria-modal="true" aria-labelledby="update-user-title">
      <div className="modal-top"><div><div className="section-eyebrow">USER MANAGEMENT · {user.id}</div><h2 id="update-user-title">Update user</h2></div><button className="modal-close" aria-label="Close" disabled={submitting} onClick={onClose}>×</button></div>
      <p className="page-subtitle">{user.fullName} · {user.email}</p>
      {actionError && <div className="alert alert-error" role="alert">{actionError}</div>}
      <form className="form-stack admin-user-form" onSubmit={(event) => { void submit(event) }}>
        <label htmlFor="update-user-role">Role<select id="update-user-role" value={role} onChange={(event) => setRole(event.target.value)}><option>Employee</option><option>Manager</option><option>Admin</option></select></label>
        <label htmlFor="update-user-department">Department<select id="update-user-department" value={departmentId} onChange={(event) => setDepartmentId(event.target.value)}><option value="">No department</option>{departmentOptions.map((department) => <option key={department.id} value={String(department.id)}>{department.name}{department.isActive ? '' : ' (Inactive)'}</option>)}</select></label>
        <div className="form-actions"><button type="button" className="button button-secondary" onClick={onClose} disabled={submitting}>Cancel</button><button className="button button-primary" disabled={submitting || !hasChanges}>{submitting ? 'Updating…' : 'Save changes'}</button></div>
      </form>
    </section>
  </div>
}

function CreateUserModal({ departments, actionError, onClose, onSubmit }: {
  departments: Department[]
  actionError: string
  onClose: () => void
  onSubmit: (input: { fullName: string; email: string; password: string; role: 'Employee' | 'Manager'; departmentId: number }) => Promise<boolean>
}) {
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirmation, setShowConfirmation] = useState(false)
  const [role, setRole] = useState<'Employee' | 'Manager'>('Employee')
  const [departmentId, setDepartmentId] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [validationError, setValidationError] = useState('')
  const dialogRef = useRef<HTMLElement | null>(null)
  const activeDepartments = departments.filter((department) => department.isActive)
  const hasUnsavedChanges = Boolean(
    fullName || email || password || confirmPassword || departmentId ||
      role !== 'Employee',
  )
  useDialogEscape(
    dialogRef,
    onClose,
    !submitting && !hasUnsavedChanges,
  )
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setValidationError('')
    if (password !== confirmPassword) {
      setValidationError('Passwords do not match.')
      return
    }
    if (password.length < 12) {
      setValidationError('Password must contain at least 12 characters.')
      return
    }
    setSubmitting(true)
    try {
      await onSubmit({
        fullName: fullName.trim(),
        email: email.trim(),
        password,
        role,
        departmentId: Number(departmentId),
      })
    } finally {
      setSubmitting(false)
    }
  }
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting && !hasUnsavedChanges) onClose() }}>
    <section ref={dialogRef} className="detail-modal admin-form-modal" role="dialog" aria-modal="true" aria-labelledby="create-user-title">
      <div className="modal-top"><div><div className="section-eyebrow">USER MANAGEMENT</div><h2 id="create-user-title">Add User</h2></div><button className="modal-close" aria-label="Close" disabled={submitting} onClick={onClose}>×</button></div>
      <p className="page-subtitle">Create an Employee or Manager account. Admin creation is not available here.</p>
      {validationError && <div className="alert alert-error" role="alert">{validationError}</div>}
      {actionError && <div className="alert alert-error" role="alert">{actionError}</div>}
      <form className="form-stack admin-user-form" onSubmit={(event) => { void submit(event) }}>
        <label>Full name <span className="required-star" aria-hidden="true"></span><input value={fullName} onChange={(event) => setFullName(event.target.value)} required maxLength={100} autoComplete="name" /></label>
        <label>Email <span className="required-star" aria-hidden="true"></span><input type="email" value={email} onChange={(event) => setEmail(event.target.value)} required maxLength={150} autoComplete="email" /></label>
        <label>Password <span className="required-star" aria-hidden="true"></span><span className="password-input-wrap"><input aria-label="Password" type={showPassword ? 'text' : 'password'} value={password} onChange={(event) => setPassword(event.target.value)} required minLength={12} autoComplete="new-password" /><button type="button" className="password-toggle" aria-label={showPassword ? 'Hide password' : 'Show password'} onClick={() => setShowPassword((shown) => !shown)}>{showPassword ? 'Hide' : 'Show'}</button></span></label>
        <label>Confirm password <span className="required-star" aria-hidden="true"></span><span className="password-input-wrap"><input aria-label="Confirm password" type={showConfirmation ? 'text' : 'password'} value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} required minLength={12} autoComplete="new-password" /><button type="button" className="password-toggle" aria-label={showConfirmation ? 'Hide confirmation' : 'Show confirmation'} onClick={() => setShowConfirmation((shown) => !shown)}>{showConfirmation ? 'Hide' : 'Show'}</button></span></label>
        <label>Role<select value={role} onChange={(event) => setRole(event.target.value as 'Employee' | 'Manager')}><option>Employee</option><option>Manager</option></select></label>
        <label>Department <span className="required-star" aria-hidden="true"></span><select value={departmentId} onChange={(event) => setDepartmentId(event.target.value)} required><option value="">Select a department</option>{activeDepartments.map((department) => <option key={department.id} value={department.id}>{department.name}</option>)}</select></label>
        <div className="form-actions"><button type="button" className="button button-secondary" onClick={onClose} disabled={submitting}>Cancel</button><button className="button button-primary" disabled={submitting || activeDepartments.length === 0}>{submitting ? 'Creating…' : 'Create user'}</button></div>
      </form>
    </section>
  </div>
}

function UserDetailsModal({ user, departments, onClose }: { user: User; departments: Department[]; onClose: () => void }) {
  const departmentName = departments.find((department) => department.id === user.departmentId)?.name ?? 'No department assigned'
  const dialogRef = useRef<HTMLElement | null>(null)
  useDialogEscape(dialogRef, onClose)
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}>
    <section ref={dialogRef} className="detail-modal" role="dialog" aria-modal="true" aria-labelledby="user-detail-title">
      <div className="modal-top"><div><div className="section-eyebrow">USER · {user.id}</div><h2 id="user-detail-title">{user.fullName}</h2></div><button className="modal-close" aria-label="Close" onClick={onClose}>×</button></div>
      <dl className="detail-grid"><div><dt>Email</dt><dd>{user.email}</dd></div><div><dt>Role</dt><dd>{user.role}</dd></div><div><dt>Department</dt><dd>{departmentName}</dd></div><div><dt>Created</dt><dd>{formatDate(user.createdAtUtc)}</dd></div></dl>
      <div className="form-actions"><button className="button button-secondary" onClick={onClose}>Close</button></div>
    </section>
  </div>
}

function DepartmentsPage({ departments, users, actionError, onCreate }: { departments: Department[]; users: User[]; actionError: string; onCreate: (name: string, code: string) => Promise<boolean> }) {
  const [showForm, setShowForm] = useState(false)
  return <><div className="welcome-row"><div><div className="eyebrow">WORKSPACE <span className="eyebrow-dot">·</span> ADMIN</div><h1>Departments</h1><p className="page-subtitle">Manage your organization's departments and membership.</p></div><button className="button button-primary" onClick={() => setShowForm(true)}><span className="button-plus">+</span> Add department</button></div>
    {showForm && <CreateDepartmentModal
      actionError={actionError}
      onClose={() => setShowForm(false)}
      onSubmit={async (departmentName, departmentCode) => {
        const succeeded = await onCreate(departmentName, departmentCode)
        if (succeeded) setShowForm(false)
        return succeeded
      }}
    />}
    <section className="department-grid">{departments.length === 0 ? <div className="content-card"><EmptyState title="No departments yet" /></div> : departments.map((department) => {
      const members = users.filter((user) => user.departmentId === department.id)
      const employeeCount = members.filter((user) => user.role.toLowerCase() === 'employee').length
      const managerCount = members.filter((user) => user.role.toLowerCase() === 'manager').length
      return <article className="department-card" key={department.id}><div className="department-symbol">{department.name.trim().charAt(0).toUpperCase() || 'D'}</div><div className="department-card-copy"><strong>{department.name}</strong><span>{department.code || 'No code assigned'}</span><span>{employeeCount} Employees · {managerCount} Managers</span></div><span className={`active-pill ${department.isActive ? 'is-active' : ''}`}>{department.isActive ? 'Active' : 'Inactive'}</span></article>
    })}</section>
  </>
}

function CreateDepartmentModal({ actionError, onClose, onSubmit }: {
  actionError: string
  onClose: () => void
  onSubmit: (name: string, code: string) => Promise<boolean>
}) {
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const dialogRef = useRef<HTMLElement | null>(null)
  const hasUnsavedChanges = Boolean(name || code)
  useDialogEscape(
    dialogRef,
    onClose,
    !submitting && !hasUnsavedChanges,
  )
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting) return
    setSubmitting(true)
    try {
      await onSubmit(name.trim(), code.trim())
    } finally {
      setSubmitting(false)
    }
  }
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting && !hasUnsavedChanges) onClose() }}>
    <section ref={dialogRef} className="detail-modal admin-form-modal" role="dialog" aria-modal="true" aria-labelledby="create-department-title">
      <div className="modal-top"><div><div className="section-eyebrow">WORKSPACE SETTINGS</div><h2 id="create-department-title">Add department</h2></div><button className="modal-close" aria-label="Close" disabled={submitting} onClick={onClose}>×</button></div>
      <p className="page-subtitle">Create a department for request ownership and Manager assignments.</p>
      {actionError && <div className="alert alert-error" role="alert">{actionError}</div>}
      <form className="form-stack admin-user-form" onSubmit={(event) => { void submit(event) }}>
        <label>Department name <span className="required-star" aria-hidden="true"></span><input value={name} onChange={(event) => setName(event.target.value)} required maxLength={100} placeholder="e.g. Operations" /></label>
        <label>Department code <span className="optional-note">Optional</span><input value={code} onChange={(event) => setCode(event.target.value)} maxLength={20} placeholder="e.g. OPS" /></label>
        <div className="form-actions"><button type="button" className="button button-secondary" onClick={onClose} disabled={submitting}>Cancel</button><button className="button button-primary" disabled={submitting}>{submitting ? 'Creating…' : 'Create department'}</button></div>
      </form>
    </section>
  </div>
}

function ProfilePage({
  profile,
  onUploadPhoto,
  onRemovePhoto,
}: {
  profile: UserProfile
  onUploadPhoto: (image: File) => Promise<void>
  onRemovePhoto: () => Promise<void>
}) {
  const [selectedPhoto, setSelectedPhoto] = useState<File | null>(null)
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const previewUrl = useMemo(
    () => selectedPhoto ? URL.createObjectURL(selectedPhoto) : null,
    [selectedPhoto],
  )

  useEffect(() => () => {
    if (previewUrl) URL.revokeObjectURL(previewUrl)
  }, [previewUrl])

  async function uploadPhoto() {
    if (!selectedPhoto) return
    setSaving(true)
    setError('')
    try {
      await onUploadPhoto(selectedPhoto)
      setSelectedPhoto(null)
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setSaving(false)
    }
  }

  async function removePhoto() {
    setSaving(true)
    setError('')
    try {
      await onRemovePhoto()
      setSelectedPhoto(null)
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setSaving(false)
    }
  }

  return <>
    <div className="welcome-row">
      <div><div className="eyebrow">ACCOUNT</div><h1>Your profile</h1><p className="page-subtitle">View your profile details and update your photo.</p></div>
    </div>
    <section className="content-card profile-card">
      <div className="profile-hero">
        <div className="profile-avatar-wrap">
          <Avatar
            name={profile.fullName}
            imageUrl={previewUrl ?? (profile.hasProfileImage
              ? profile.profileImageUrl
              : null)}
            large
          />
          <span className="profile-avatar-camera" aria-hidden="true">
            <svg viewBox="0 0 24 24"><path d="M4 7h3l1.5-2h7L17 7h3v12H4z" /><circle cx="12" cy="13" r="3.5" /></svg>
          </span>
        </div>
        <div className="profile-hero-copy"><h2>{profile.fullName}</h2><span className="role-chip">{roleName(profile.role)}</span><p>Manage your account photo. Other profile details are read-only.</p></div>
        <div className="profile-photo-actions">
          <label className="button button-secondary profile-photo-picker">
            <svg aria-hidden="true" viewBox="0 0 24 24"><path d="M4 7h3l1.5-2h7L17 7h3v12H4z" /><circle cx="12" cy="13" r="3.5" /></svg> Change photo
            <input
              type="file"
              accept="image/jpeg,image/png,image/webp"
              aria-label="Choose profile photo"
              onChange={(event) => setSelectedPhoto(event.target.files?.[0] ?? null)}
              disabled={saving}
            />
          </label>
          {selectedPhoto && <button
            className="button button-primary"
            type="button"
            onClick={() => { void uploadPhoto() }}
            disabled={saving}
          >{saving ? 'Uploading…' : 'Upload photo'}</button>}
          {profile.hasProfileImage && <button
            className="profile-remove-photo"
            type="button"
            onClick={() => { void removePhoto() }}
            disabled={saving}
          ><svg aria-hidden="true" viewBox="0 0 24 24"><path d="M4 7h16M9 7V4h6v3m3 0-.8 13H6.8L6 7m4 4v5m4-5v5" /></svg> Remove photo</button>}
          <span className="field-hint">JPEG, PNG, or WebP · up to 2 MB</span>
        </div>
      </div>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      <div className="profile-details-grid">
        <div className="profile-personal-details">
          <div className="profile-readonly-field">
            <span className="profile-detail-label"><span aria-hidden="true">♙</span> Full name</span>
            <div className="profile-value-input">{profile.fullName}</div>
            <small>Your account name is managed by your workspace administrator.</small>
          </div>
          <div className="profile-readonly-field">
            <span className="profile-detail-label"><span aria-hidden="true">⌕</span> Phone number</span>
            <div className="profile-value-input">{profile.phoneNumber || 'Not provided'}</div>
            <small>Contact your workspace administrator if this needs to be changed.</small>
          </div>
        </div>
        <div className="profile-account-details">
          <div className="profile-account-detail">
            <span className="profile-detail-label"><span aria-hidden="true">✉</span> Email address</span>
            <div className="profile-value-input">{profile.email}</div>
            <small>Email address cannot be changed here.</small>
          </div>
          <div className="profile-account-detail">
            <span className="profile-detail-label"><span aria-hidden="true">▣</span> Workspace role</span>
            <div className="profile-value-input">{roleName(profile.role)}</div>
            <small>Workspace role cannot be changed here.</small>
          </div>
          <div className="profile-account-detail">
            <span className="profile-detail-label"><span aria-hidden="true">⌂</span> Department</span>
            <div className="profile-value-input">{profile.departmentName ?? 'Not assigned'}</div>
            <small>Department cannot be changed here.</small>
          </div>
        </div>
      </div>
    </section>
  </>
}

function ToastViewport({
  toasts,
  onDismiss,
}: {
  toasts: ToastMessage[]
  onDismiss: (id: number) => void
}) {
  return <div className="toast-viewport" aria-label="Notifications">
    {toasts.map((toast) => <ToastCard
      key={toast.id}
      toast={toast}
      onDismiss={onDismiss}
    />)}
  </div>
}

function ToastCard({
  toast,
  onDismiss,
}: {
  toast: ToastMessage
  onDismiss: (id: number) => void
}) {
  useEffect(() => {
    const duration = toast.kind === 'success'
      ? 4000
      : toast.kind === 'warning'
        ? 5000
        : 6000
    const timeout = window.setTimeout(
      () => onDismiss(toast.id),
      duration,
    )
    return () => window.clearTimeout(timeout)
  }, [toast.id, toast.kind, onDismiss])

  return <div className={`toast-card toast-${toast.kind}`} role={
    toast.kind === 'error' ? 'alert' : 'status'
  }>
    <span>{toast.text}</span>
    <button
      aria-label={`Dismiss ${toast.kind} notification`}
      onClick={() => onDismiss(toast.id)}
    >×</button>
  </div>
}

function WorkflowNotifications({
  onError,
  onNavigateToRequest,
  onRequestNotification,
  onIncomingNotification,
  unreadCount,
  onUnreadCountChange,
}: {
  onError: (message: string) => void
  onNavigateToRequest: (requestId: number) => Promise<void>
  onRequestNotification: (requestId: number, notificationType: string) => Promise<void>
  onIncomingNotification: (notification: WorkflowNotification) => void
  unreadCount: number
  onUnreadCountChange: (count: number) => void
}) {
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(
    Boolean(sessionStorage.getItem('flowdesk-token')),
  )
  const [notifications, setNotifications] = useState<WorkflowNotification[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [realtimeState, setRealtimeState] = useState<
    'connecting' | 'connected' | 'reconnecting' | 'disconnected'
  >('connecting')
  const [listError, setListError] = useState('')
  const receivedNotificationIds = useRef(new Set<number>())

  const refreshNotifications = useCallback(async (
    requestedPage = 1,
    append = false,
  ) => {
    try {
      const result = await getNotifications(requestedPage)
      setNotifications((current) => {
  const merged = append
    ? [...current, ...result.items]
    : result.items

  return Array.from(
    new Map(merged.map((item) => [item.id, item])).values(),
  )
})
      onUnreadCountChange(result.unreadCount)
      setTotalCount(result.totalCount)
      setPage(result.page)
    } catch (caught) {
      setListError(messageFrom(caught))
    } finally {
      setLoading(false)
    }
  }, [onUnreadCountChange])

  useEffect(() => {
    let stopped = false
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', {
        accessTokenFactory: () => sessionStorage.getItem('flowdesk-token') ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('notificationReceived', (notification: WorkflowNotification) => {
      setListError('')
      const isNewNotification = !receivedNotificationIds.current.has(
        notification.id,
      )
      receivedNotificationIds.current.add(notification.id)
      setNotifications((current) => [
        notification,
        ...current.filter((item) => item.id !== notification.id),
      ])
      if (isNewNotification) {
        onIncomingNotification(notification)
      }
      if (notification.requestId != null) {
        void onRequestNotification(notification.requestId, notification.type)
      }
      void refreshNotifications()
    })
    connection.onreconnecting(() => setRealtimeState('reconnecting'))
    connection.onreconnected(() => {
      setRealtimeState('connected')
      setListError('')
      void refreshNotifications()
    })
    connection.onclose(() => {
      setRealtimeState('disconnected')
      if (!stopped) void startConnection()
    })

    async function startConnection() {
      while (!stopped) {
        try {
          await connection.start()
          setRealtimeState('connected')
          return
        } catch {
          setRealtimeState('reconnecting')
          await new Promise((resolve) => window.setTimeout(resolve, 5000))
        }
      }
    }

    void startConnection()
    return () => {
      stopped = true
      void connection.stop()
    }
  }, [onIncomingNotification, onRequestNotification, refreshNotifications])

  async function toggle() {
    const nextOpen = !open
    setOpen(nextOpen)
    if (!nextOpen) return
    setLoading(true)
    setListError('')
    await refreshNotifications()
  }

  async function activateNotification(notification: WorkflowNotification) {
    if (!notification.readAtUtc) {
      try {
        await markNotificationRead(notification.id)
        setNotifications((current) => current.map((item) => item.id === notification.id
          ? { ...item, readAtUtc: new Date().toISOString() }
          : item))
        onUnreadCountChange(Math.max(0, unreadCount - 1))
      } catch (caught) {
        const message = messageFrom(caught)
        setListError(message)
        onError(message)
        return
      }
    }

    if (notification.requestId != null) {
      await onNavigateToRequest(notification.requestId)
    }
  }

  return <div className="notification-menu">
    <button className="notification-trigger" aria-label={`Notifications${unreadCount ? `, ${unreadCount} unread` : ''}`} aria-expanded={open} onClick={() => { void toggle() }}>
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9m-8 12h4" /></svg>
      {unreadCount > 0 && <span className="notification-count">{unreadCount}</span>}
    </button>
    {open && <section className="notification-popover" aria-label="Workflow notifications">
      <div className="notification-heading">
        <strong>Notifications</strong>
        <span className={`notification-connection ${realtimeState}`}>{realtimeState}</span>
        <button className="text-button" onClick={() => setOpen(false)}>Close</button>
      </div>
      {listError && <p className="notification-error" role="alert">{listError}</p>}
      {loading && notifications.length === 0
        ? <p className="notification-empty">Loading notifications…</p> : notifications.length === 0
        ? <p className="notification-empty">You’re all caught up.</p>
        : <ul>{notifications.map((notification) => <li key={notification.id} className={notification.readAtUtc ? '' : 'unread'}>
          <button className="notification-item" onClick={() => { void activateNotification(notification) }}>
            <strong className="notification-item-title">
              {notification.type === 'RequestNeedsAdminAssignment' ||
                notification.type === 'RequestSubmitted'
                ? 'New Approval Request'
                : notification.type.replace(/([a-z])([A-Z])/g, '$1 $2')}
            </strong>
            <span>{notification.message}</span>
            <small>{formatDate(notification.createdAtUtc)}{notification.readAtUtc ? '' : ' · Unread'}</small>
          </button>
        </li>)}</ul>}
      {notifications.length < totalCount && <button
        className="notification-load-more"
        disabled={loading}
        onClick={() => {
          setLoading(true)
          setListError('')
          void refreshNotifications(page + 1, true)
        }}
      >{loading ? 'Loading…' : 'Load older notifications'}</button>}
    </section>}
  </div>
}

function RequestDetails({ item, workflow, role, hasCancellationRequest, busy, actionError, users, departments, onClose, onAction, onRequestCancellation, onAssign }: {
  item: RequestItem
  workflow: WorkflowInstance | null
  role: string
  hasCancellationRequest: boolean
  busy: boolean
  actionError: string
  users: User[]
  departments: Department[]
  onClose: () => void
  onAction: (action: 'approve' | 'reject' | 'cancel', comment?: string) => Promise<boolean>
  onRequestCancellation: (reason: string) => Promise<boolean>
  onAssign: (managerId: number) => Promise<boolean>
}) {
  const [confirmAction, setConfirmAction] = useState<'approve' | 'reject' | 'cancel' | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [showCancellationForm, setShowCancellationForm] = useState(false)
  const [cancellationReason, setCancellationReason] = useState('')
  const [cancellationError, setCancellationError] = useState('')
  const [showAssign, setShowAssign] = useState(false)
  const [decisionComment, setDecisionComment] = useState('')
  const [decisionError, setDecisionError] = useState('')
  const [currentTime] = useState(() => Date.now())
  const dialogRef = useRef<HTMLElement | null>(null)
  const isPending = item.status.toLowerCase() === 'pending'
  const canRequestLeaveCancellation = role === 'Employee' &&
    item.status.toLowerCase() === 'approved' &&
    item.requestTypeCode === 'LEAVE' &&
    !item.requestDetails?.cancellationOfRequestId &&
    !hasCancellationRequest
  const activeAssignment = item.assignments?.some((assignment) => assignment.status.toLowerCase() === 'pending') ?? false
  const canManagerReview = role === 'Manager' && isPending && activeAssignment
  const canAdminAssign = role === 'Admin' && isPending && !activeAssignment && item.departmentId != null
  const activeDepartment = departments.find((department) => department.id === item.departmentId && department.isActive)
  const eligibleManagers = users.filter((user) =>
    user.role.toLowerCase() === 'manager' &&
    user.isActive !== false &&
    user.departmentId === item.departmentId &&
    activeDepartment?.isActive)
  useDialogEscape(
    dialogRef,
    () => {
      if (showCancellationForm) {
        setShowCancellationForm(false)
        setCancellationReason('')
        setCancellationError('')
        return
      }
      if (confirmAction) {
        setConfirmAction(null)
        setDecisionComment('')
        setDecisionError('')
      } else {
        onClose()
      }
    },
    !busy && !submitting,
  )
  async function confirm() {
    if (!confirmAction || submitting || busy) return
    if (confirmAction === 'reject' && workflow && !decisionComment.trim()) {
      setDecisionError('Add a rejection reason before submitting this workflow decision.')
      return
    }
    setSubmitting(true)
    setDecisionError('')
    let succeeded = false
    try {
      succeeded = await onAction(confirmAction, decisionComment.trim() || undefined)
      if (succeeded) onClose()
    } finally {
      setSubmitting(false)
      if (succeeded) setConfirmAction(null)
    }
  }
  async function submitCancellation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const reason = cancellationReason.trim()
    if (!reason) {
      setCancellationError('Enter a reason for cancelling this approved leave.')
      return
    }
    setSubmitting(true)
    setCancellationError('')
    let succeeded = false
    try {
      succeeded = await onRequestCancellation(reason)
      if (succeeded) onClose()
    } finally {
      setSubmitting(false)
      if (succeeded) setShowCancellationForm(false)
    }
  }
  return (
    <div className="modal-backdrop request-detail-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy && !submitting && !confirmAction && !showAssign && !showCancellationForm) onClose() }}>
      <section ref={dialogRef} className="detail-modal request-detail-drawer" role="dialog" aria-modal="true" aria-labelledby="detail-title">
        <div className="modal-top">
          <div className="section-eyebrow">REQUEST · REQ-{String(item.id).padStart(4, '0')}</div>
          <button className="modal-close" onClick={onClose} disabled={busy || submitting} aria-label="Close details">×</button>
        </div>
        <div className="detail-title-row">
          <div><h2 id="detail-title">{item.title}</h2><p>Submitted {formatDate(item.createdAtUtc)}{item.priority ? ` · ${item.priority} priority` : ''}</p></div>
          <StatusBadge status={item.status} />
        </div>
        <dl className="request-detail-grid">
          <div><dt>Submitted by</dt><dd>{item.createdByName || item.submittedBy || 'Unknown / Legacy Request'}{item.createdByEmail && <small className="detail-subtext">{item.createdByEmail}</small>}</dd></div>
          <div><dt>Department</dt><dd>{item.departmentName || item.department || 'Unknown department'}</dd></div>
          <div><dt>Request type</dt><dd>{item.requestTypeName || 'Legacy request'}</dd></div>
          <div><dt>Amount</dt><dd>{money(item.amount)}</dd></div>
          <div><dt>Current status</dt><dd><StatusBadge status={item.status} /></dd></div>
        </dl>
        {item.requestDetails && <RequestSpecificDetails item={item} />}
        <section className="request-description" aria-labelledby="request-description-heading">
          <h3 id="request-description-heading">Description</h3>
          <p>{item.description?.trim() || 'No description provided.'}</p>
        </section>
        <RequestAttachmentsPanel
          requestId={item.id}
          canUpload={role === 'Employee' && isPending}
          canRemove={role === 'Admin' || (role === 'Employee' && isPending)}
        />
        {workflow && <section className="workflow-progress">
          <div className="workflow-progress-heading">
            <div><div className="section-eyebrow">ACTIVE WORKFLOW · V{workflow.workflowVersion}</div><strong>{workflow.workflowName}</strong></div>
            <span className={`workflow-status status-${workflow.status.toLowerCase()}`}>{workflow.status}</span>
          </div>
          <ol className="workflow-timeline">
            {[...workflow.stages].sort((left, right) => left.sequence - right.sequence).map((stage) => <li key={stage.id} className={`workflow-timeline-stage stage-${stage.status.toLowerCase()}`}>
              <span className="workflow-timeline-dot" />
              <div className="workflow-stage-copy">
                <div className="workflow-stage-title"><strong>Stage {stage.sequence} · {stage.approverName}</strong><StatusBadge status={stage.status} /></div>
                {stage.status === 'Active' && <span className="workflow-current-stage">Current active stage</span>}
                <span>{stage.activatedAtUtc ? `Activated ${formatDateTime(stage.activatedAtUtc)}` : 'Not yet active'}{stage.decidedAtUtc ? ` · Decided ${formatDateTime(stage.decidedAtUtc)}` : ''}</span>
                {stage.dueAtUtc && stage.status === 'Active' && <span className={new Date(stage.dueAtUtc).getTime() < currentTime ? 'workflow-overdue' : ''}>{new Date(stage.dueAtUtc).getTime() < currentTime ? 'Overdue · ' : 'Due · '}{formatDate(stage.dueAtUtc)}</span>}
                {stage.comment && <p className="workflow-stage-comment"><strong>{stage.status === 'Rejected' ? 'Rejection reason' : 'Approval comment'}:</strong> {stage.comment}</p>}
              </div>
            </li>)}
          </ol>
          <div className="workflow-final-outcome">
            <strong>Final outcome</strong>
            <StatusBadge status={item.status} />
          </div>
        </section>}
        <div className="detail-history">
          <div className="section-eyebrow">ASSIGNMENT &amp; APPROVAL HISTORY</div>
          {item.assignments && item.assignments.length > 0
            ? item.assignments.map((assignment) => <div className="assignment-history-row" key={assignment.id}><div><strong>{assignment.status === 'Approved' ? 'Approved by ' : assignment.status === 'Rejected' ? 'Rejected by ' : assignment.status === 'Cancelled' ? 'Cancelled for ' : 'Assigned to '}{assignment.approverName || 'Manager'}</strong>{assignment.approverEmail && <span>{assignment.approverEmail}</span>}<span>Assigned {formatDate(assignment.assignedAtUtc)}{assignment.decidedAtUtc ? ` · Decision ${formatDate(assignment.decidedAtUtc)}` : ''}</span></div><StatusBadge status={assignment.status} /></div>)
            : <p className="detail-muted">No assignment history is recorded for this request.</p>}
        </div>
        {!workflow && <p className="detail-disclaimer">This request uses the legacy manual-assignment workflow.</p>}
        {actionError && <div className="alert alert-error detail-action-error" role="alert">{actionError}</div>}
        {!confirmAction && <div className="detail-actions">
          {role === 'Manager' && canManagerReview && <><button className="button button-secondary reject-button" onClick={() => setConfirmAction('reject')}>Reject</button><button className="button button-primary" onClick={() => setConfirmAction('approve')}>Approve request <span>→</span></button></>}
          {role === 'Employee' && isPending && <button className="button button-secondary reject-button" onClick={() => setConfirmAction('cancel')}>Cancel request</button>}
          {canRequestLeaveCancellation && <button className="button button-secondary reject-button" onClick={() => setShowCancellationForm(true)}>Request leave cancellation</button>}
          {canAdminAssign && <button className="button button-primary" onClick={() => setShowAssign(true)}>Assign Manager</button>}
        </div>}
        {showCancellationForm && <form className="confirmation-panel" onSubmit={(event) => { void submitCancellation(event) }}>
          <strong>Request cancellation approval?</strong>
          <p>Your approved leave will remain scheduled until a manager approves this cancellation.</p>
          <label className="decision-comment-label">Cancellation reason<textarea value={cancellationReason} onChange={(event) => { setCancellationReason(event.target.value); setCancellationError('') }} maxLength={2000} rows={3} required placeholder="Explain why you need to cancel this leave…" /></label>
          {cancellationError && <div className="alert alert-error" role="alert">{cancellationError}</div>}
          {actionError && <div className="alert alert-error" role="alert">{actionError}</div>}
          <div><button type="button" className="button button-secondary" disabled={submitting} onClick={() => { setShowCancellationForm(false); setCancellationReason(''); setCancellationError('') }}>Go back</button><button className="button button-primary" disabled={submitting}>{submitting ? 'Submitting…' : 'Submit cancellation request'}</button></div>
        </form>}
        {confirmAction && <div className="confirmation-panel" role="group" aria-label="Confirm request action">
          <strong>Confirm {confirmAction}?</strong>
          <p>{confirmAction === 'approve' ? 'Approve this stage and continue the workflow.' : confirmAction === 'reject' ? 'Reject this request and record an optional reason.' : 'This will cancel your request.'}</p>
          {role === 'Manager' && <label className="decision-comment-label">Decision comment {confirmAction === 'reject' ? '(reason)' : '(optional)'}<textarea value={decisionComment} onChange={(event) => { setDecisionComment(event.target.value); setDecisionError('') }} maxLength={2000} rows={3} placeholder={confirmAction === 'reject' ? 'Explain why this request is being rejected…' : 'Add context for this decision…'} /></label>}
          {decisionError && <div className="alert alert-error" role="alert">{decisionError}</div>}
          <div><button className="button button-secondary" disabled={submitting || busy} onClick={() => { setConfirmAction(null); setDecisionComment('') }}>Go back</button><button className={`button ${confirmAction === 'approve' ? 'button-primary' : 'button-secondary reject-button'}`} disabled={submitting || busy} onClick={() => { void confirm() }}>{submitting || busy ? 'Saving…' : `Confirm ${confirmAction}`}</button></div>
        </div>}
        {canAdminAssign && !activeDepartment && <p className="inline-information">The request department is inactive or unavailable, so no assignment can be made.</p>}
        {showAssign && <ManagerAssignmentModal
          request={item}
          managers={eligibleManagers}
          departments={departments}
          actionError={actionError}
          onClose={() => setShowAssign(false)}
          onAssign={async (_requestId, managerId) => {
            const succeeded = await onAssign(managerId)
            if (succeeded) setShowAssign(false)
            return succeeded
          }}
        />}
      </section>
    </div>
  )
}

function RequestSpecificDetails({ item }: { item: RequestItem }) {
  const details = item.requestDetails
  if (!details) return null
  const entries = item.requestTypeCode === 'MATERIAL_PURCHASE'
    ? [
      ['Item name', details.itemName],
      ['Quantity', details.quantity],
      ['Business justification', details.businessJustification],
    ]
    : item.requestTypeCode === 'LEAVE'
      ? [
        ['Leave type', details.leaveType],
        ['Start date', details.startDate ? formatDate(details.startDate) : null],
        ['End date', details.endDate ? formatDate(details.endDate) : null],
        ['Calendar days', details.totalLeaveDays],
        ['Leave units', details.calculatedLeaveUnits],
        ['Reason', details.reason],
      ]
      : item.requestTypeCode === 'EXPENSE_REIMBURSEMENT'
        ? [
          ['Expense category', details.category],
          ['Expense date', details.expenseDate ? formatDate(details.expenseDate) : null],
          ['Business justification', details.businessJustification],
        ]
        : item.requestTypeCode === 'IT_ACCESS'
          ? [
            ['System or application', details.systemApplication],
            ['Requested access level', details.requestedAccessLevel],
            ['Duration', details.durationDays ? `${details.durationDays} days` : 'Not specified'],
            ['Business justification', details.businessJustification],
          ]
          : item.requestTypeCode === 'TRAVEL'
            ? [
              ['Destination', details.destination],
              ['Start date', details.startDate ? formatDate(details.startDate) : null],
              ['End date', details.endDate ? formatDate(details.endDate) : null],
              ['Business purpose', details.businessPurpose],
            ]
            : item.requestTypeCode === 'WORK_FROM_HOME'
              ? [
                ['Start date', details.startDate ? formatDate(details.startDate) : null],
                ['End date', details.endDate ? formatDate(details.endDate) : null],
                ['Reason', details.reason],
              ]
              : []
  const visibleEntries = entries.filter(([, value]) => value != null)
  if (visibleEntries.length === 0) return null
  return <section className="request-description" aria-labelledby="request-specific-details-heading">
    <h3 id="request-specific-details-heading">Request details</h3>
    <dl className="request-detail-grid">{visibleEntries.map(([label, value]) =>
      <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
  </section>
}

function ManagerAssignmentModal({ request, managers, departments, actionError, onClose, onAssign }: {
  request: RequestItem
  managers: User[]
  departments: Department[]
  actionError: string
  onClose: () => void
  onAssign: (requestId: number, managerId: number) => Promise<boolean>
}) {
  const [managerId, setManagerId] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const dialogRef = useRef<HTMLElement | null>(null)
  useDialogEscape(dialogRef, onClose, !submitting)
  const department = departments.find((item) => item.id === request.departmentId)
  const eligibleManagers = managers.filter((manager) =>
    manager.role.toLowerCase() === 'manager' &&
    manager.departmentId === request.departmentId &&
    department?.isActive)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!managerId || submitting) return
    setSubmitting(true)
    try {
      await onAssign(request.id, Number(managerId))
    } finally {
      setSubmitting(false)
    }
  }
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting) onClose() }}>
    <section ref={dialogRef} className="detail-modal admin-form-modal manager-assignment-modal" role="dialog" aria-modal="true" aria-labelledby="assign-manager-title">
      <div className="modal-top"><div><div className="section-eyebrow">ASSIGNMENT · REQ-{String(request.id).padStart(4, '0')}</div><h2 id="assign-manager-title">Assign Manager</h2></div><button className="modal-close" aria-label="Close" disabled={submitting} onClick={onClose}>×</button></div>
      <dl className="detail-grid"><div><dt>Request</dt><dd>{request.title}</dd></div><div><dt>Department</dt><dd>{request.departmentName || request.department || 'Unknown department'}</dd></div></dl>
      {actionError && <div className="alert alert-error detail-action-error" role="alert">{actionError}</div>}
      {eligibleManagers.length === 0
        ? <div className="inline-information">No eligible Manager found. Assign a Manager to this department first.</div>
        : <form className="form-stack manager-assignment-form" onSubmit={(event) => { void submit(event) }}>
          <label>Manager<select value={managerId} onChange={(event) => setManagerId(event.target.value)} required disabled={submitting}><option value="">Select a Manager</option>{eligibleManagers.map((manager) => <option key={manager.id} value={manager.id}>{manager.fullName} · {manager.email}</option>)}</select></label>
          <div className="form-actions"><button type="button" className="button button-secondary" onClick={onClose} disabled={submitting}>Cancel</button><button className="button button-primary" disabled={!managerId || submitting}>{submitting ? 'Assigning…' : 'Assign Manager'}</button></div>
        </form>}
    </section>
  </div>
}

function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase()
  const label = normalized.charAt(0).toUpperCase() + normalized.slice(1)
  return <span className={`status-badge status-${normalized}`}><i />{label}</span>
}

function Avatar({
  name,
  imageUrl,
  large = false,
}: {
  name: string
  imageUrl?: string | null
  large?: boolean
}) {
  const [loadedImage, setLoadedImage] = useState<{
    sourceUrl: string
    objectUrl: string
  } | null>(null)
  const [failedImageUrl, setFailedImageUrl] = useState<string | null>(null)
  const imageSource = loadedImage !== null &&
    loadedImage.sourceUrl === imageUrl &&
    failedImageUrl !== imageUrl
    ? loadedImage.objectUrl
    : null
  const initials = name.trim().split(/\s+/).slice(0, 2).map((part) => part[0]).join('').toUpperCase()

  useEffect(() => {
    let cancelled = false
    let objectUrl: string | null = null
    if (!imageUrl) return

    const token = sessionStorage.getItem('flowdesk-token')
    void fetch(imageUrl, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    }).then(async (response) => {
      if (!response.ok) {
        if (!cancelled) setFailedImageUrl(imageUrl)
        return
      }
      const blob = await response.blob()
      if (cancelled) return
      objectUrl = URL.createObjectURL(blob)
      setFailedImageUrl(null)
      setLoadedImage({ sourceUrl: imageUrl, objectUrl })
    }).catch(() => {
      if (!cancelled) setFailedImageUrl(imageUrl)
    })

    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [imageUrl])

  return <span className={`avatar ${large ? 'avatar-large' : ''}`}>
    {imageSource
      ? <img src={imageSource} alt="" aria-hidden="true" />
      : initials || 'U'}
  </span>
}

function EmptyState({ title }: { title: string }) {
  return <div className="empty-state"><span className="empty-icon">—</span><strong>{title}</strong><p>There’s nothing to show here yet.</p></div>
}

export default App
