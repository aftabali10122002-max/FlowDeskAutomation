import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import {
  ApiError,
  assignRequest,
  createAdminUser,
  createDepartment,
  createRequest,
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
  updateUserRole,
  updateUserDepartment,
  updateUserActive,
  type Department,
  type RequestItem,
  type User,
  type UserProfile,
  type WorkflowInstance,
  type WorkflowNotification,
} from './api'
import { WorkflowManagement } from './WorkflowManagement'
import './App.css'

type Page = 'Overview' | 'My Requests' | 'New Request' | 'Assigned to Me' | 'Request History' | 'User Management' | 'Departments' | 'All Requests' | 'Assignment Management' | 'Workflow Management' | 'Profile'
type Notice = { kind: 'success' | 'error'; text: string }

const rolePages: Record<string, Page[]> = {
  Employee: ['Overview', 'My Requests', 'New Request', 'Profile'],
  Manager: ['Overview', 'Assigned to Me', 'Request History', 'Profile'],
  Admin: ['Overview', 'All Requests', 'Assignment Management', 'Workflow Management', 'User Management', 'Departments', 'Profile'],
}

function roleName(role: string) {
  return role.toLowerCase() === 'admin' ? 'Admin' : role.toLowerCase() === 'manager' ? 'Manager' : 'Employee'
}

function formatDate(date: string) {
  const parsed = new Date(date)
  return Number.isNaN(parsed.getTime())
    ? '—'
    : new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' }).format(parsed)
}

function money(amount: number) {
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
    'My Requests': 'M8 3h8l4 4v14H4V3h4Zm0 0v5h8V3M8 13h8M8 17h5',
    'New Request': 'M12 5v14M5 12h14',
    'Assigned to Me': 'M9 12l2 2 4-4M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
    'Request History': 'M3 12a9 9 0 1 0 2.6-6.4L3 8m0-5v5h5m4-1v5l3 2',
    'User Management': 'M16 20v-1a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v1m6-9a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm7-7a4 4 0 0 1 0 7m1 4a4 4 0 0 1 3 4v1',
    Departments: 'M3 21h18M5 21V7l7-4 7 4v14M9 9h1m4 0h1m-6 4h1m4 0h1m-6 4h1m4 0h1',
    'All Requests': 'M4 5h16M4 12h16M4 19h16M8 5v0m0 7v0m0 7v0',
    'Assignment Management': 'M8 7h12M8 12h12M8 17h12M4 7h.01M4 12h.01M4 17h.01',
    'Workflow Management': 'M4 5h6v5H4zM14 14h6v5h-6zM10 7h4a2 2 0 0 1 2 2v5m-2 0 2 2 2-2',
    Profile: 'M20 21a8 8 0 0 0-16 0m8-10a5 5 0 1 0 0-10 5 5 0 0 0 0 10Z',
  }
  return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d={pathByPage[page]} /></svg>
}

function App() {
  const [profile, setProfile] = useState<UserProfile | null>(null)
  const [token, setToken] = useState(() => sessionStorage.getItem('flowdesk-token') ?? '')
  const [page, setPage] = useState<Page>('Overview')
  const [requests, setRequests] = useState<RequestItem[]>([])
  const [requestsLoadFailed, setRequestsLoadFailed] = useState(false)
  const [users, setUsers] = useState<User[]>([])
  const [departments, setDepartments] = useState<Department[]>([])
  const [notificationUnreadCount, setNotificationUnreadCount] = useState(0)
  const [loading, setLoading] = useState(Boolean(sessionStorage.getItem('flowdesk-token')))
  const [error, setError] = useState('')
  const [notice, setNotice] = useState<Notice | null>(null)
  const [selectedRequest, setSelectedRequest] = useState<RequestItem | null>(null)
  const [selectedWorkflow, setSelectedWorkflow] = useState<WorkflowInstance | null>(null)
  const [assignmentTarget, setAssignmentTarget] = useState<RequestItem | null>(null)
  const [selectedUser, setSelectedUser] = useState<User | null>(null)
  const [pendingActionId, setPendingActionId] = useState<number | null>(null)
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)

  const clearSession = useCallback(() => {
    sessionStorage.removeItem('flowdesk-token')
    setToken('')
    setProfile(null)
    setRequests([])
    setRequestsLoadFailed(false)
    setSelectedRequest(null)
    setSelectedWorkflow(null)
    setUsers([])
    setDepartments([])
    setNotificationUnreadCount(0)
    setLoading(false)
  }, [])

  const refresh = useCallback(async (currentRole: string) => {
    setError('')
    let requestData: RequestItem[]
    try {
      requestData = await getRequests()
      setRequestsLoadFailed(false)
    } catch (caught) {
      setRequestsLoadFailed(true)
      throw caught
    }
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
      setUsers(userData)
      setDepartments(departmentData)
    }
    try {
      const notificationPage = await getNotifications()
      setNotificationUnreadCount(notificationPage.unreadCount)
    } catch (caught) {
      setError(`Notifications could not be loaded. ${messageFrom(caught)}`)
    }
  }, [])

  const authenticate = useCallback(async (jwt: string) => {
    sessionStorage.setItem('flowdesk-token', jwt)
    setToken(jwt)
    setPage('Overview')
  }, [])

  useEffect(() => {
    if (!token) return
    let active = true
    Promise.all([getMe()]).then(async ([currentUser]) => {
      if (!active) return
      setProfile(currentUser)
      setPage('Overview')
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
  }, [token, clearSession, refresh])

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
    try {
      const [request, workflow] = await Promise.all([
        getRequest(item.id),
        getRequestWorkflow(item.id),
      ])
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

    try {
      const [request, workflow] = await Promise.all([
        getRequest(requestId),
        getRequestWorkflow(requestId),
      ])
      setSelectedRequest(request)
      setSelectedWorkflow(workflow)
    } catch (caught) {
      setError(messageFrom(caught))
    }
  }

  function signOut() {
    clearSession()
    setPage('Overview')
    setError('')
  }

  async function runAction(action: () => Promise<void>, success: string) {
    setNotice(null)
    setError('')
    try {
      await action()
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 401) {
        clearSession()
        setError('Your session has expired. Please sign in again.')
      }
      else if (caught instanceof ApiError && caught.status === 403) setError('You do not have permission to perform this action.')
      else setError(messageFrom(caught))
      return false
    }
    setNotice({ kind: 'success', text: success })
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
    setPendingActionId(id)
    setError('')
    setNotice(null)
    try {
      await patchRequest(id, action, comment)
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
          setNotice({ kind: 'success', text: message })
        } catch (caught) {
          setError(`Decision saved, but the latest request list could not be loaded. ${messageFrom(caught)}`)
        }
      } else {
        setNotice({ kind: 'success', text: 'Decision recorded successfully.' })
      }
      return true
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 401) {
        clearSession()
        setError('Your session has expired. Please sign in again.')
      } else if (caught instanceof ApiError && caught.status === 403) {
        setError('You do not have permission to perform this action.')
      } else {
        setError(messageFrom(caught))
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
      {mobileMenuOpen && <button className="mobile-nav-scrim" aria-label="Close navigation" onClick={() => setMobileMenuOpen(false)} />}
      <aside className="sidebar">
        <a className="brand" href="#" aria-label="FlowDesk home" onClick={(event) => { event.preventDefault(); setPage('Overview') }}>
          <span className="brand-mark"><span>F</span></span>
          <span className="brand-name">FlowDesk</span>
        </a>
        <div className="sidebar-workspace"><span className="workspace-dot" /> Approval workspace</div>
        <nav className="side-nav" aria-label="Main navigation">
          {pages.map((item) => (
            <button className={`nav-link ${visiblePage === item ? 'active' : ''}`} key={item} title={navLabel(item)} aria-label={item} onClick={() => { setPage(item); setError(''); setNotice(null); setMobileMenuOpen(false) }}>
              <NavIcon page={item} />
              <span>{navLabel(item)}</span>
              {item === 'Assigned to Me' && counts.pending > 0 && <span className="nav-count">{counts.pending}</span>}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <button className="nav-link logout-nav-button" onClick={signOut}><svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M10 17l5-5-5-5m5 5H3m10-9h6a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-6" /></svg><span>Sign out</span></button>
          <button className="sidebar-profile" onClick={() => setPage('Profile')} aria-label={`Open profile for ${profile.fullName}`}>
            <Avatar name={profile.fullName} />
            <span className="sidebar-profile-copy"><strong>{profile.fullName}</strong><small>{roleName(profile.role)}</small></span>
            <span className="sidebar-profile-more" aria-hidden="true">···</span>
          </button>
        </div>
      </aside>

      <main className="main-panel">
        <header className="topbar">
          <div className="topbar-left">
            <button className="sidebar-toggle" aria-label={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'} onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}><span /><span /><span /></button>
            <button className="mobile-menu-toggle" aria-label="Open navigation" onClick={() => setMobileMenuOpen(true)}><span /><span /><span /></button>
            <div className="breadcrumbs"><span>Workspace</span><span className="crumb-separator">/</span><strong>{navLabel(visiblePage)}</strong></div>
          </div>
          <div className="topbar-actions">
            <span className="topbar-role">{roleName(profile.role)} workspace</span>
            <WorkflowNotifications
              onError={setError}
              onNavigateToRequest={openNotificationRequest}
              unreadCount={notificationUnreadCount}
              onUnreadCountChange={setNotificationUnreadCount}
            />
            <button className="header-profile-button" aria-label={`Profile for ${profile.fullName}`} onClick={() => setPage('Profile')}><Avatar name={profile.fullName} /></button>
          </div>
        </header>

        <div className="page-content">
          {isRequestPage(visiblePage) && <div className="request-page-tabs">
            {requestPagesForRole(roleName(profile.role)).map((requestPage) => <button className={visiblePage === requestPage.page ? 'selected' : ''} key={requestPage.page} onClick={() => setPage(requestPage.page)}>{requestPage.label}</button>)}
          </div>}
          {error && <div className="alert alert-error" role="alert"><span>{error}</span><button onClick={() => setError('')} aria-label="Dismiss error">×</button></div>}
          {notice && <div className={`alert alert-${notice.kind}`} role="status"><span>{notice.text}</span><button onClick={() => setNotice(null)} aria-label="Dismiss message">×</button></div>}
          {loading && <div className="loading-strip"><span className="spinner" /> Loading your workspace…</div>}
          {visiblePage === 'Overview' && <Overview profile={profile} requests={requests} requestsLoadFailed={requestsLoadFailed} counts={counts} users={users} departments={departments} onOpenRequest={(item) => { void openRequest(item) }} onNavigate={setPage} />}
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
              onCreate={() => setPage('New Request')}
            />
          )}
          {visiblePage === 'New Request' && <NewRequest onSubmit={(title, amount, priority) => runAction(async () => { await createRequest(title, amount, priority); setPage('My Requests') }, 'Your request has been submitted.')} />}
          {visiblePage === 'Workflow Management' && <WorkflowManagement departments={departments} users={users} />}
          {visiblePage === 'User Management' && <UserManagement
            users={users}
            departments={departments}
            actionError={error}
            onCreateUser={(input) => runAction(async () => { await createAdminUser(input) }, 'User created successfully.')}
            onRoleChange={(userId, role) => runAction(async () => { await updateUserRole(userId, role) }, 'User role updated.')}
            onDepartmentChange={(userId, departmentId) => runAction(async () => { await updateUserDepartment(userId, departmentId) }, 'User department updated.')}
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
          {visiblePage === 'Profile' && <ProfilePage profile={profile} />}
        </div>
        <footer className="page-footer"><span>FlowDesk Approval Management</span><span>Secure approvals, clearly managed.</span></footer>
      </main>

      {selectedRequest && <RequestDetails
        item={selectedRequest}
        workflow={selectedWorkflow}
        role={roleName(profile.role)}
        busy={pendingActionId === selectedRequest.id}
        actionError={error}
        departments={departments}
        users={users}
        onClose={() => { setSelectedRequest(null); setSelectedWorkflow(null) }}
        onAction={(action, comment) => decideRequest(selectedRequest.id, action, comment)}
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

function Overview({ profile, requests, requestsLoadFailed, counts, users, departments, onOpenRequest, onNavigate }: { profile: UserProfile; requests: RequestItem[]; requestsLoadFailed: boolean; counts: { total: number; pending: number; approved: number; rejected: number; cancelled: number }; users: User[]; departments: Department[]; onOpenRequest: (item: RequestItem) => void; onNavigate: (page: Page) => void }) {
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
          { label: 'Total Users', value: users.length, tone: 'teal', icon: 'U', context: 'Workspace members' },
          { label: 'Active Departments', value: departments.filter((department) => department.isActive).length, tone: 'blue', icon: 'D', context: 'Currently operating' },
        ]
  return (
    <>
      <div className="welcome-row dashboard-welcome"><div><div className="eyebrow">OVERVIEW <span className="eyebrow-dot">·</span> {role.toUpperCase()}</div><h1>{greeting}, {profile.fullName.split(' ')[0]}</h1><p className="page-subtitle">Here's what's happening with your approval workflow.</p></div>
        <div className="dashboard-actions"><time className="today-pill">{new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' }).format(today)}</time>
          {role === 'Employee' && <button className="button button-primary" onClick={() => onNavigate('New Request')}><span className="button-plus">+</span> New request</button>}
          {role === 'Manager' && <button className="button button-primary" onClick={() => onNavigate('Assigned to Me')}>Review requests <span>→</span></button>}
          {role === 'Admin' && <button className="button button-primary" onClick={() => onNavigate('User Management')}>Manage workspace <span>→</span></button>}
        </div>
      </div>
      <section className="stat-grid" aria-label="Summary statistics">
        {cards.map((card) => <article className="stat-card" key={card.label}><div className="stat-top"><span className={`stat-icon ${card.tone}`}>{card.icon}</span><span className="stat-context">{card.context}</span></div><div className="stat-value">{card.value}</div><div className="stat-label">{card.label}</div></article>)}
      </section>
      <section className="content-card recent-card">
        <div className="card-heading"><div><div className="section-eyebrow">{role === 'Manager' ? 'YOUR QUEUE' : role === 'Admin' ? 'ACTIVITY' : 'YOUR WORK'}</div><h2>{role === 'Manager' ? 'Recently assigned' : 'Recent requests'}</h2></div><button className="text-button" onClick={() => onNavigate(role === 'Manager' ? 'Assigned to Me' : role === 'Admin' ? 'All Requests' : 'My Requests')}>View all <span>→</span></button></div>
        <RequestTable requests={[...requests].sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()).slice(0, 5)} onOpen={onOpenRequest} compact loadFailed={requestsLoadFailed && requests.length === 0} />
      </section>
    </>
  )
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
  return (
    <>
      <div className="welcome-row request-page-heading"><div><div className="eyebrow">WORKSPACE <span className="eyebrow-dot">·</span> {role.toUpperCase()}</div><h1>{page === 'Assignment Management' ? 'Assignment Management' : page}</h1><p className="page-subtitle">{page === 'Assignment Management' ? 'Route pending requests to an eligible Manager in the same department.' : page === 'All Requests' ? 'Monitor requests and assignment status across departments.' : 'Review request status and approval history.'}</p></div></div>
      <div className="request-workspace">
        <aside className="status-filter-panel">
          <div className="filter-panel-heading"><strong>Status Type</strong><span>{pageRequests.length}</span></div>
          <button className={`status-filter-button ${activeStatus === 'All statuses' ? 'selected' : ''}`} onClick={() => setFilterStatus('All statuses')}>All requests <span>{pageRequests.length}</span></button>
          {tabs.filter((tab) => !tab.startsWith('All')).map((tab) => {
            const count = pageRequests.filter((item) => item.status.toLowerCase() === tab.toLowerCase()).length
            return <button className={`status-filter-button ${activeStatus === tab ? 'selected' : ''}`} key={tab} onClick={() => setFilterStatus(tab)}>{tab}<span>{count}</span></button>
          })}
          {role === 'Employee' && <button className="button button-primary filter-create-button" onClick={onCreate}>New request</button>}
          {role === 'Manager' && <p className="unassigned-note">Requests without a Manager assignment are routed to an Admin for assignment.</p>}
          {role === 'Admin' && page === 'Assignment Management' && <p className="unassigned-note">Assignment is available only for pending requests without an active Manager assignment.</p>}
        </aside>
        <section className="content-card request-list-card">
          <div className="request-search-row">
            <label className="search-field"><span className="search-symbol">Search</span><input value={query} onChange={(event) => { setQuery(event.target.value); setCurrentPage(1) }} placeholder="Search title, submitter, department…" aria-label="Search requests" /></label>
            {role === 'Admin' && <label className="request-department-filter">Department<select value={departmentId} onChange={(event) => { setDepartmentId(event.target.value); setCurrentPage(1) }}><option>All departments</option>{departments.map((department) => <option key={department.id} value={String(department.id)}>{department.name}</option>)}</select></label>}
            <span className="result-count">{filtered.length} results</span>
          </div>
          {errorMessageForEmpty(role, pageRequests) && <div className="inline-information">{errorMessageForEmpty(role, pageRequests)}</div>}
          <RequestTable requests={visibleRequests} onOpen={onOpen} role={role} onAction={onAction} onAssign={onAssign} busyRequestId={busyRequestId} loadFailed={requestsLoadFailed && requests.length === 0} />
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

function RequestTable({ requests, onOpen, compact = false, role, onAction, onAssign, busyRequestId = null, loadFailed = false }: {
  requests: RequestItem[]
  onOpen: (item: RequestItem) => void
  compact?: boolean
  role?: string
  onAction?: (id: number, action: 'approve' | 'reject' | 'cancel') => Promise<boolean>
  onAssign?: (request: RequestItem) => void
  busyRequestId?: number | null
  loadFailed?: boolean
}) {
  if (requests.length === 0) return <div className={`empty-state ${compact ? 'compact-empty' : ''}`}><span className="empty-icon">—</span><strong>{loadFailed ? 'Requests unavailable' : role === 'Manager' ? 'No assigned requests' : 'No requests yet'}</strong><p>{loadFailed ? 'Requests could not be loaded. Check the error above and try again.' : role === 'Manager' ? 'New items appear here after an Admin assigns them to you.' : 'When requests are available, they’ll appear here.'}</p></div>
  return (
    <div className="table-scroll"><table className={`request-table ${role === 'Manager' ? 'manager-request-table' : ''}`}><thead><tr><th>ID</th><th>REQUEST TITLE</th><th>SUBMITTED BY</th><th>DEPARTMENT</th>{role === 'Admin' && <th>ASSIGNED MANAGER</th>}<th>AMOUNT</th><th>SUBMITTED</th><th>STATUS</th><th>ACTION</th></tr></thead><tbody>
      {requests.map((item) => {
        const hasPendingAssignment = item.assignments?.some((assignment) => assignment.status.toLowerCase() === 'pending') ?? false
        const canDecide = role === 'Manager' && item.status.toLowerCase() === 'pending' && hasPendingAssignment
        const canCancel = role === 'Employee' && item.status.toLowerCase() === 'pending'
        const assignmentNames = item.assignments?.map((assignment) => assignment.approverName).join(', ') || '—'
        const isBusy = busyRequestId === item.id
        return <tr key={item.id} className="clickable-row">
          <td className="request-id">REQ-{String(item.id).padStart(4, '0')}</td>
          <td><div className="request-title"><span className="request-title-copy"><strong>{item.title}</strong>{item.priority && <small className={`priority-label priority-${item.priority.toLowerCase()}`}>{item.priority}</small>}</span></div></td>
          <td>{item.createdByName || item.submittedBy || 'Legacy request'}</td>
          <td>{item.departmentName || item.department || '—'}</td>
          {role === 'Admin' && <td>{assignmentNames}</td>}
          <td className="amount-cell">{money(item.amount)}</td>
          <td>{formatDate(item.createdAtUtc)}</td>
          <td><StatusBadge status={item.status} /></td>
          <td className="row-actions">
            {canDecide
              ? <button className="row-action-button review-action" onClick={() => onOpen(item)}>Review</button>
              : <button className="row-action-button" onClick={() => onOpen(item)}>View</button>}
            {canCancel && onAction && <button className="row-action-button reject-action" disabled={isBusy} onClick={() => { void onAction(item.id, 'cancel') }}>Cancel</button>}
            {role === 'Admin' && item.status.toLowerCase() === 'pending' && !hasPendingAssignment && onAssign && <button className="row-action-button review-action" onClick={() => onAssign(item)}>Assign Manager</button>}
            {role === 'Admin' && item.status.toLowerCase() === 'pending' && hasPendingAssignment && <span className="assigned-label">Assigned</span>}
          </td>
        </tr>
      })}
    </tbody></table></div>
  )
}

function NewRequest({ onSubmit }: { onSubmit: (title: string, amount: number, priority: NonNullable<RequestItem['priority']>) => Promise<boolean> }) {
  const [title, setTitle] = useState('')
  const [amount, setAmount] = useState('')
  const [priority, setPriority] = useState<NonNullable<RequestItem['priority']>>('Medium')
  const [submitting, setSubmitting] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    try { await onSubmit(title.trim(), Number(amount), priority) } finally { setSubmitting(false) }
  }
  return <><div className="welcome-row"><div><div className="eyebrow">MY REQUESTS <span className="eyebrow-dot">·</span> NEW</div><h1>New request</h1><p className="page-subtitle">Share the details of your approval request.</p></div></div><section className="content-card form-card"><div className="card-heading"><div><div className="section-eyebrow">REQUEST DETAILS</div><h2>What do you need approved?</h2></div><span className="required-note">* Required</span></div><form className="form-stack request-form" onSubmit={submit}><label>Request title <span className="required-star">*</span><input value={title} onChange={(event) => setTitle(event.target.value)} placeholder="e.g. Laptop replacement" required /></label><label>Amount <span className="required-star">*</span><div className="amount-input"><input type="number" min="0.01" step="0.01" value={amount} onChange={(event) => setAmount(event.target.value)} placeholder="0.00" required /></div></label><label>Priority<select value={priority} onChange={(event) => setPriority(event.target.value as NonNullable<RequestItem['priority']>)}><option>Low</option><option>Medium</option><option>High</option><option>Urgent</option></select></label><p className="form-hint">Your request will be submitted to the existing approval workflow.</p><div className="form-actions"><button type="reset" className="button button-secondary" onClick={() => { setTitle(''); setAmount(''); setPriority('Medium') }}>Clear</button><button className="button button-primary" disabled={submitting}>{submitting ? 'Submitting…' : 'Submit request'} <span>→</span></button></div></form></section></>
}

function UserManagement({ users, departments, actionError, onCreateUser, onRoleChange, onDepartmentChange, onActiveChange, onViewUser }: {
  users: User[]
  departments: Department[]
  actionError: string
  onCreateUser: (input: { fullName: string; email: string; password: string; role: 'Employee' | 'Manager'; departmentId: number }) => Promise<boolean>
  onRoleChange: (id: number, role: string) => Promise<boolean>
  onDepartmentChange: (id: number, departmentId: number | null) => Promise<boolean>
  onActiveChange: (id: number, isActive: boolean) => Promise<boolean>
  onViewUser: (id: number) => void
}) {
  const [showCreateForm, setShowCreateForm] = useState(false)
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
          <td><div className="request-title"><Avatar name={user.fullName} /><span><strong>{user.fullName}</strong><small>{user.email}</small></span></div></td>
          <td><select className="role-select" aria-label={`Role for ${user.fullName}`} value={user.role} onChange={(event) => { void onRoleChange(user.id, event.target.value) }}><option>Employee</option><option>Manager</option><option>Admin</option></select></td>
          <td><select className="role-select" aria-label={`Department for ${user.fullName}`} value={user.departmentId == null ? '' : String(user.departmentId)} onChange={(event) => { void onDepartmentChange(user.id, event.target.value ? Number(event.target.value) : null) }}><option value="">No department</option>{departments.filter((department) => department.isActive).map((department) => <option key={department.id} value={String(department.id)}>{department.name}</option>)}</select></td>
          <td><span className={`user-active-status ${user.isActive ? 'active' : 'inactive'}`}>{user.isActive ? 'Active' : 'Inactive'}</span></td>
          <td>{formatDate(user.createdAtUtc)}</td>
          <td className="row-actions"><button className="row-action-button" onClick={() => onViewUser(user.id)}>View</button><button className={`row-action-button ${user.isActive ? 'reject-action' : 'review-action'}`} onClick={() => { void onActiveChange(user.id, !user.isActive) }}>{user.isActive ? 'Deactivate' : 'Activate'}</button></td>
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
  </>
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
  const activeDepartments = departments.filter((department) => department.isActive)
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
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting) onClose() }}>
    <section className="detail-modal admin-form-modal" role="dialog" aria-modal="true" aria-labelledby="create-user-title">
      <div className="modal-top"><div><div className="section-eyebrow">USER MANAGEMENT</div><h2 id="create-user-title">Add User</h2></div><button className="modal-close" aria-label="Close" onClick={onClose}>×</button></div>
      <p className="page-subtitle">Create an Employee or Manager account. Admin creation is not available here.</p>
      {validationError && <div className="alert alert-error" role="alert">{validationError}</div>}
      {actionError && <div className="alert alert-error" role="alert">{actionError}</div>}
      <form className="form-stack admin-user-form" onSubmit={(event) => { void submit(event) }}>
        <label>Full name<input value={fullName} onChange={(event) => setFullName(event.target.value)} required maxLength={100} autoComplete="name" /></label>
        <label>Email<input type="email" value={email} onChange={(event) => setEmail(event.target.value)} required maxLength={150} autoComplete="email" /></label>
        <label>Password<span className="password-input-wrap"><input aria-label="Password" type={showPassword ? 'text' : 'password'} value={password} onChange={(event) => setPassword(event.target.value)} required minLength={12} autoComplete="new-password" /><button type="button" className="password-toggle" aria-label={showPassword ? 'Hide password' : 'Show password'} onClick={() => setShowPassword((shown) => !shown)}>{showPassword ? 'Hide' : 'Show'}</button></span></label>
        <label>Confirm password<span className="password-input-wrap"><input aria-label="Confirm password" type={showConfirmation ? 'text' : 'password'} value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} required minLength={12} autoComplete="new-password" /><button type="button" className="password-toggle" aria-label={showConfirmation ? 'Hide confirmation' : 'Show confirmation'} onClick={() => setShowConfirmation((shown) => !shown)}>{showConfirmation ? 'Hide' : 'Show'}</button></span></label>
        <label>Role<select value={role} onChange={(event) => setRole(event.target.value as 'Employee' | 'Manager')}><option>Employee</option><option>Manager</option></select></label>
        <label>Department<select value={departmentId} onChange={(event) => setDepartmentId(event.target.value)} required><option value="">Select a department</option>{activeDepartments.map((department) => <option key={department.id} value={department.id}>{department.name}</option>)}</select></label>
        <div className="form-actions"><button type="button" className="button button-secondary" onClick={onClose} disabled={submitting}>Cancel</button><button className="button button-primary" disabled={submitting || activeDepartments.length === 0}>{submitting ? 'Creating…' : 'Create user'}</button></div>
      </form>
    </section>
  </div>
}

function UserDetailsModal({ user, departments, onClose }: { user: User; departments: Department[]; onClose: () => void }) {
  const departmentName = departments.find((department) => department.id === user.departmentId)?.name ?? 'No department assigned'
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}>
    <section className="detail-modal" role="dialog" aria-modal="true" aria-labelledby="user-detail-title">
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
  return <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting) onClose() }}>
    <section className="detail-modal admin-form-modal" role="dialog" aria-modal="true" aria-labelledby="create-department-title">
      <div className="modal-top"><div><div className="section-eyebrow">WORKSPACE SETTINGS</div><h2 id="create-department-title">Add department</h2></div><button className="modal-close" aria-label="Close" disabled={submitting} onClick={onClose}>×</button></div>
      <p className="page-subtitle">Create a department for request ownership and Manager assignments.</p>
      {actionError && <div className="alert alert-error" role="alert">{actionError}</div>}
      <form className="form-stack admin-user-form" onSubmit={(event) => { void submit(event) }}>
        <label>Department name<input value={name} onChange={(event) => setName(event.target.value)} required maxLength={100} placeholder="e.g. Operations" /></label>
        <label>Department code <span className="optional-note">Optional</span><input value={code} onChange={(event) => setCode(event.target.value)} maxLength={20} placeholder="e.g. OPS" /></label>
        <div className="form-actions"><button type="button" className="button button-secondary" onClick={onClose} disabled={submitting}>Cancel</button><button className="button button-primary" disabled={submitting}>{submitting ? 'Creating…' : 'Create department'}</button></div>
      </form>
    </section>
  </div>
}

function ProfilePage({ profile }: { profile: UserProfile }) {
  return <><div className="welcome-row"><div><div className="eyebrow">ACCOUNT</div><h1>Your profile</h1><p className="page-subtitle">Your account information and workspace access.</p></div></div><section className="content-card profile-card"><div className="profile-hero"><Avatar name={profile.fullName} large /><div><div className="section-eyebrow">FLOWDESK MEMBER</div><h2>{profile.fullName}</h2><span className="role-chip">{roleName(profile.role)}</span></div></div><div className="profile-fields"><div><span>Full name</span><strong>{profile.fullName}</strong></div><div><span>Email address</span><strong>{profile.email}</strong></div><div><span>Workspace role</span><strong>{roleName(profile.role)}</strong></div></div><p className="profile-readonly">Profile details are read-only because the current API does not support profile editing.</p></section></>
}

function WorkflowNotifications({
  onError,
  onNavigateToRequest,
  unreadCount,
  onUnreadCountChange,
}: {
  onError: (message: string) => void
  onNavigateToRequest: (requestId: number) => Promise<void>
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

  const refreshNotifications = useCallback(async (
    requestedPage = 1,
    append = false,
  ) => {
    try {
      const result = await getNotifications(requestedPage)
      setNotifications((current) => append
        ? [...current, ...result.items.filter(
          (item) => !current.some((existing) => existing.id === item.id),
        )]
        : result.items)
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
      setNotifications((current) => [
        notification,
        ...current.filter((item) => item.id !== notification.id),
      ])
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
  }, [refreshNotifications])

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
            <span>{notification.message}</span><small>{formatDate(notification.createdAtUtc)} · {notification.type}{notification.readAtUtc ? '' : ' · Unread'}</small>
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

function RequestDetails({ item, workflow, role, busy, actionError, users, departments, onClose, onAction, onAssign }: {
  item: RequestItem
  workflow: WorkflowInstance | null
  role: string
  busy: boolean
  actionError: string
  users: User[]
  departments: Department[]
  onClose: () => void
  onAction: (action: 'approve' | 'reject' | 'cancel', comment?: string) => Promise<boolean>
  onAssign: (managerId: number) => Promise<boolean>
}) {
  const [confirmAction, setConfirmAction] = useState<'approve' | 'reject' | 'cancel' | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [showAssign, setShowAssign] = useState(false)
  const [decisionComment, setDecisionComment] = useState('')
  const [decisionError, setDecisionError] = useState('')
  const [currentTime] = useState(() => Date.now())
  const isPending = item.status.toLowerCase() === 'pending'
  const activeAssignment = item.assignments?.some((assignment) => assignment.status.toLowerCase() === 'pending') ?? false
  const canManagerReview = role === 'Manager' && isPending && activeAssignment
  const canAdminAssign = role === 'Admin' && isPending && !activeAssignment && item.departmentId != null
  const activeDepartment = departments.find((department) => department.id === item.departmentId && department.isActive)
  const eligibleManagers = users.filter((user) =>
    user.role.toLowerCase() === 'manager' &&
    user.isActive !== false &&
    user.departmentId === item.departmentId &&
    activeDepartment?.isActive)
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
  return (
    <div className="modal-backdrop request-detail-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onClose() }}>
      <section className="detail-modal request-detail-drawer" role="dialog" aria-modal="true" aria-labelledby="detail-title">
        <div className="modal-top">
          <div className="section-eyebrow">REQUEST · REQ-{String(item.id).padStart(4, '0')}</div>
          <button className="modal-close" onClick={onClose} disabled={busy} aria-label="Close details">Close</button>
        </div>
        <div className="detail-title-row">
          <div><h2 id="detail-title">{item.title}</h2><p>Submitted {formatDate(item.createdAtUtc)}{item.priority ? ` · ${item.priority} priority` : ''}</p></div>
          <StatusBadge status={item.status} />
        </div>
        <dl className="request-detail-grid">
          <div><dt>Submitted by</dt><dd>{item.createdByName || item.submittedBy || 'Unknown / Legacy Request'}{item.createdByEmail && <small className="detail-subtext">{item.createdByEmail}</small>}</dd></div>
          <div><dt>Department</dt><dd>{item.departmentName || item.department || 'Unknown department'}</dd></div>
          <div><dt>Amount</dt><dd>{money(item.amount)}</dd></div>
          <div><dt>Current status</dt><dd><StatusBadge status={item.status} /></dd></div>
        </dl>
        {workflow && <section className="workflow-progress">
          <div className="workflow-progress-heading">
            <div><div className="section-eyebrow">ACTIVE WORKFLOW · V{workflow.workflowVersion}</div><strong>{workflow.workflowName}</strong></div>
            <span className={`workflow-status status-${workflow.status.toLowerCase()}`}>{workflow.status}</span>
          </div>
          <ol className="workflow-timeline">
            {workflow.stages.map((stage) => <li key={stage.id} className={`workflow-timeline-stage stage-${stage.status.toLowerCase()}`}>
              <span className="workflow-timeline-dot" />
              <div className="workflow-stage-copy">
                <div className="workflow-stage-title"><strong>Stage {stage.sequence} · {stage.approverName}</strong><StatusBadge status={stage.status} /></div>
                <span>{stage.activatedAtUtc ? `Activated ${formatDate(stage.activatedAtUtc)}` : 'Not yet active'}{stage.decidedAtUtc ? ` · Decided ${formatDate(stage.decidedAtUtc)}` : ''}</span>
                {stage.dueAtUtc && stage.status === 'Active' && <span className={new Date(stage.dueAtUtc).getTime() < currentTime ? 'workflow-overdue' : ''}>{new Date(stage.dueAtUtc).getTime() < currentTime ? 'Overdue · ' : 'Due · '}{formatDate(stage.dueAtUtc)}</span>}
                {stage.comment && <p className="workflow-stage-comment">{stage.comment}</p>}
              </div>
            </li>)}
          </ol>
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
          {canAdminAssign && <button className="button button-primary" onClick={() => setShowAssign(true)}>Assign Manager</button>}
        </div>}
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
    <section className="detail-modal admin-form-modal" role="dialog" aria-modal="true" aria-labelledby="assign-manager-title">
      <div className="modal-top"><div><div className="section-eyebrow">ASSIGNMENT · REQ-{String(request.id).padStart(4, '0')}</div><h2 id="assign-manager-title">Assign Manager</h2></div><button className="modal-close" aria-label="Close" disabled={submitting} onClick={onClose}>×</button></div>
      <dl className="detail-grid"><div><dt>Request</dt><dd>{request.title}</dd></div><div><dt>Department</dt><dd>{request.departmentName || request.department || 'Unknown department'}</dd></div></dl>
      {actionError && <div className="alert alert-error detail-action-error" role="alert">{actionError}</div>}
      {eligibleManagers.length === 0
        ? <div className="inline-information">No eligible Manager found. Assign a Manager to this department first.</div>
        : <form className="form-stack admin-user-form" onSubmit={(event) => { void submit(event) }}>
          <label>Manager<select value={managerId} onChange={(event) => setManagerId(event.target.value)} required><option value="">Select a Manager</option>{eligibleManagers.map((manager) => <option key={manager.id} value={manager.id}>{manager.fullName} · {manager.email}</option>)}</select></label>
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

function Avatar({ name, large = false }: { name: string; large?: boolean }) {
  const initials = name.trim().split(/\s+/).slice(0, 2).map((part) => part[0]).join('').toUpperCase()
  return <span className={`avatar ${large ? 'avatar-large' : ''}`}>{initials || 'U'}</span>
}

function EmptyState({ title }: { title: string }) {
  return <div className="empty-state"><span className="empty-icon">—</span><strong>{title}</strong><p>There’s nothing to show here yet.</p></div>
}

export default App
