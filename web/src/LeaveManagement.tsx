import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import {
  ApiError,
  addLeaveHoliday,
  addLeavePayPeriod,
  assignLeavePolicy,
  createAdminLeavePolicy,
  createAdminLeaveType,
  createLeaveHolidayCalendar,
  createLeavePayrollCalendar,
  createLeaveDesignation,
  createLeaveWorkSchedule,
  createLeaveAdjustment,
  createTypedRequest,
  getAdminLeavePolicies,
  getAdminLeaveTypes,
  getDepartments,
  getEmployeeLeaveBalances,
  getEmployeeLeaveMetadata,
  getLeaveAdjustmentAudit,
  getLeaveDesignations,
  getLeaveHolidayCalendars,
  getLeavePayrollCalendars,
  getLeaveTypes,
  getLeaveWorkSchedules,
  getMyLeaveBalances,
  getMyLeaveHistory,
  getRequests,
  getTeamLeaveBalances,
  getUsers,
  previewMyLeave,
  publishAdminLeavePolicy,
  saveEmployeeLeaveMetadata,
  updateAdminLeaveType,
  type Department,
  type EmployeeLeaveMetadata,
  type LeaveBalance,
  type LeaveDesignation,
  type LeaveHolidayCalendar,
  type LeaveLedgerItem,
  type LeavePayrollCalendar,
  type LeavePolicy,
  type LeavePolicyDraftInput,
  type LeavePreview,
  type LeaveSchedule,
  type LeaveTeamBalance,
  type LeaveTypeInfo,
  type RequestItem,
  type User,
} from './api'
import './LeaveManagement.css'

type Role = 'Employee' | 'Manager' | 'Admin'
type AdminTab =
  | 'Overview'
  | 'Leave Types'
  | 'Policies'
  | 'Employee Profiles'
  | 'Balances & Adjustments'
  | 'Work Schedules'
  | 'Holiday Calendars'
  | 'Payroll Calendars'
const CURRENT_YEAR = new Date().getFullYear()

function readableError(error: unknown) {
  return error instanceof ApiError
    ? error.message
    : 'Unable to load leave information. Please try again.'
}

function today() {
  const now = new Date()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

function units(value: number) {
  return new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 2,
  }).format(value)
}

function dateLabel(value: string | null | undefined) {
  if (!value) return '—'
  const parsed = new Date(`${value.slice(0, 10)}T00:00:00`)
  return Number.isNaN(parsed.getTime())
    ? value
    : new Intl.DateTimeFormat(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(parsed)
}

function statusClass(value: string) {
  const status = value.toLowerCase()
  if (status.includes('approve') || status.includes('publish')) return 'is-positive'
  if (status.includes('reject') || status.includes('expire')) return 'is-negative'
  if (status.includes('pending') || status.includes('draft')) return 'is-pending'
  return 'is-neutral'
}

function fetchMyLeavePageData() {
  return Promise.all([
    getMyLeaveBalances(),
    getMyLeaveHistory(),
    getLeaveTypes(),
    getRequests(),
  ])
}

function fetchAdminLeavePageData(
  initialUsers: User[],
  initialDepartments: Department[],
) {
  return Promise.all([
    getAdminLeaveTypes(),
    getAdminLeavePolicies(),
    getLeaveWorkSchedules(),
    getLeaveHolidayCalendars(),
    getLeavePayrollCalendars(),
    getLeaveDesignations(),
    initialUsers.length ? Promise.resolve(initialUsers) : getUsers(),
    initialDepartments.length ? Promise.resolve(initialDepartments) : getDepartments(),
  ])
}

export function LeaveManagement({
  role,
  users = [],
  departments = [],
  onOpenRequest,
}: {
  role: Role
  users?: User[]
  departments?: Department[]
  onOpenRequest: () => void
}) {
  if (role === 'Admin') {
    return <AdminLeaveManagement users={users} departments={departments} />
  }
  if (role === 'Manager') return <TeamLeave />
  return <MyLeave onOpenRequest={onOpenRequest} />
}

function MyLeave({ onOpenRequest }: { onOpenRequest: () => void }) {
  const [balances, setBalances] = useState<LeaveBalance[]>([])
  const [history, setHistory] = useState<LeaveLedgerItem[]>([])
  const [types, setTypes] = useState<LeaveTypeInfo[]>([])
  const [requests, setRequests] = useState<RequestItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [leaveType, setLeaveType] = useState('')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [startHalf, setStartHalf] = useState(false)
  const [endHalf, setEndHalf] = useState(false)
  const [reason, setReason] = useState('')
  const [preview, setPreview] = useState<LeavePreview | null>(null)
  const [previewError, setPreviewError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(async () => {
    try {
      const [balanceData, historyData, typeData, requestData] =
        await Promise.all([
          getMyLeaveBalances(),
          getMyLeaveHistory(),
          getLeaveTypes(),
          getRequests(),
        ])
      setBalances(balanceData)
      setHistory(historyData)
      setTypes(typeData)
      setRequests(requestData)
      setLeaveType((current) => current || typeData[0]?.code || '')
    } catch (caught) {
      setError(readableError(caught))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    let current = true
    fetchMyLeavePageData()
      .then(([balanceData, historyData, typeData, requestData]) => {
        if (!current) return
        setBalances(balanceData)
        setHistory(historyData)
        setTypes(typeData)
        setRequests(requestData)
        setLeaveType((value) => value || typeData[0]?.code || '')
      })
      .catch((caught: unknown) => {
        if (current) setError(readableError(caught))
      })
      .finally(() => {
        if (current) setLoading(false)
      })
    return () => { current = false }
  }, [])

  const upcoming = useMemo(() => {
    const currentDate = today()
    return requests.filter((item) =>
      item.requestTypeCode?.toUpperCase() === 'LEAVE' &&
      item.status.toLowerCase() === 'approved' &&
      !item.requestDetails?.cancellationOfRequestId &&
      Boolean(item.requestDetails?.startDate) &&
      (item.requestDetails?.startDate ?? '') >= currentDate)
      .sort((left, right) =>
        (left.requestDetails?.startDate ?? '')
          .localeCompare(right.requestDetails?.startDate ?? ''))
  }, [requests])

  async function requestPreview() {
    setPreview(null)
    setPreviewError('')
    try {
      const result = await previewMyLeave({
        leaveType,
        startDate,
        endDate,
        startDayPortion: startHalf ? 'HalfDay' : 'FullDay',
        endDayPortion: endHalf ? 'HalfDay' : 'FullDay',
      })
      setPreview(result)
    } catch (caught) {
      setPreviewError(readableError(caught))
    }
  }

  async function submitLeave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitting(true)
    setNotice('')
    setError('')
    try {
      await createTypedRequest({
        requestTypeCode: 'LEAVE',
        priority: 'Medium',
        description: reason,
        leave: {
          leaveType,
          startDate,
          endDate,
          reason,
          startDayPortion: startHalf ? 'HalfDay' : 'FullDay',
          endDayPortion: endHalf ? 'HalfDay' : 'FullDay',
        },
      })
      setNotice('Your leave request was submitted for approval.')
      setStartDate('')
      setEndDate('')
      setReason('')
      setPreview(null)
      await load()
    } catch (caught) {
      setError(readableError(caught))
    } finally {
      setSubmitting(false)
    }
  }

  const total = balances.reduce((sum, item) => sum + item.available, 0)
  const totals = balances.reduce(
    (value, item) => ({
      entitlement: value.entitlement + item.earned + item.carryForward,
      earned: value.earned + item.earned,
      used: value.used + item.used,
      pending: value.pending + item.pending,
      scheduled: value.scheduled + item.scheduled,
    }),
    { entitlement: 0, earned: 0, used: 0, pending: 0, scheduled: 0 },
  )

  return (
    <section className="leave-page">
      <header className="leave-page-heading">
        <div>
          <p className="leave-eyebrow">TIME AWAY</p>
          <h1>My Leave</h1>
          <p>Understand your balance, upcoming time away, and request progress.</p>
        </div>
        <button className="leave-button leave-button-primary" onClick={onOpenRequest}>
          View my requests
        </button>
      </header>
      {error && <div className="leave-alert" role="alert">{error}</div>}
      {notice && <div className="leave-notice" role="status">{notice}</div>}
      {loading ? (
        <div className="leave-loading" role="status">Loading your leave…</div>
      ) : (
        <>
          <div className="leave-summary-grid">
            {[
              ['Total entitlement', totals.entitlement],
              ['Earned leave', totals.earned],
              ['Used leave', totals.used],
              ['Pending leave', totals.pending],
              ['Scheduled leave', totals.scheduled],
              ['Available leave', total],
            ].map(([label, value]) => (
              <article className="leave-summary-card" key={label}>
                <span>{label}</span>
                <strong>{units(Number(value))}</strong>
                <small>days across enabled leave types</small>
              </article>
            ))}
          </div>
          <div className="leave-content-grid">
            <section className="leave-panel">
              <div className="leave-panel-heading">
                <div><h2>Balances by leave type</h2><p>Current posted balance and commitments</p></div>
              </div>
              {balances.length === 0 ? (
                <div className="leave-empty">No leave balances are available yet.</div>
              ) : (
                <div className="leave-type-cards">
                  {balances.map((balance) => (
                    <article className="leave-type-card" key={balance.leaveTypeId}>
                      <div className="leave-type-card-top">
                        <span className="leave-type-mark">{balance.leaveTypeCode.slice(0, 2)}</span>
                        <span className="leave-status is-positive">Active</span>
                      </div>
                      <h3>{balance.leaveTypeName}</h3>
                      <strong>{units(balance.available)} <small>available</small></strong>
                      <div className="leave-meter"><span style={{ width: `${Math.min(100, Math.max(0, balance.earned > 0 ? balance.available / balance.earned * 100 : 0))}%` }} /></div>
                      <p>{units(balance.used)} used · {units(balance.pending)} pending · {units(balance.scheduled)} scheduled</p>
                    </article>
                  ))}
                </div>
              )}
            </section>
            <section className="leave-panel">
              <div className="leave-panel-heading">
                <div><h2>Upcoming approved leave</h2><p>Approved requests beginning today or later</p></div>
              </div>
              {upcoming.length === 0 ? (
                <div className="leave-empty">No upcoming approved leave.</div>
              ) : (
                <ul className="leave-upcoming-list">
                  {upcoming.map((item) => (
                    <li key={item.id}>
                      <span className="leave-date-tile">{dateLabel(item.requestDetails?.startDate).split(' ')[0]}</span>
                      <div><strong>{item.requestDetails?.leaveType ?? item.title}</strong><small>{dateLabel(item.requestDetails?.startDate)} – {dateLabel(item.requestDetails?.endDate)}</small></div>
                      <span className={`leave-status ${statusClass(item.status)}`}>{item.status}</span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
          <section className="leave-panel leave-request-panel" id="request-leave">
            <div className="leave-panel-heading">
              <div><h2>Request leave</h2><p>Preview uses your current published policy. Final calculation is checked again at submission.</p></div>
              <span className="leave-step-number">01</span>
            </div>
            <form className="leave-form-grid" onSubmit={(event) => void submitLeave(event)}>
              <label>Leave type
                <select required value={leaveType} onChange={(event) => { setLeaveType(event.target.value); setPreview(null) }}>
                  {types.map((item) => <option key={item.id} value={item.code}>{item.displayName}</option>)}
                </select>
              </label>
              <label>Start date<input required type="date" value={startDate} onChange={(event) => { setStartDate(event.target.value); setPreview(null) }} /></label>
              <label>End date<input required type="date" min={startDate || undefined} value={endDate} onChange={(event) => { setEndDate(event.target.value); setPreview(null) }} /></label>
              <label className="leave-check"><input type="checkbox" checked={startHalf} onChange={(event) => { setStartHalf(event.target.checked); setPreview(null) }} /> First day is a half day</label>
              <label className="leave-check"><input type="checkbox" checked={endHalf} onChange={(event) => { setEndHalf(event.target.checked); setPreview(null) }} /> Last day is a half day</label>
              <label className="leave-form-wide">Reason<textarea required maxLength={2000} rows={3} value={reason} onChange={(event) => setReason(event.target.value)} /></label>
              <div className="leave-form-actions leave-form-wide">
                <button type="button" className="leave-button" disabled={!leaveType || !startDate || !endDate || endDate < startDate} onClick={() => void requestPreview()}>Calculate preview</button>
                <button type="submit" className="leave-button leave-button-primary" disabled={submitting || !leaveType || !startDate || !endDate}>{submitting ? 'Submitting…' : 'Submit leave request'}</button>
              </div>
              {previewError && <div className="leave-alert leave-form-wide" role="alert">{previewError}</div>}
              {preview && <div className="leave-preview leave-form-wide" role="status"><strong>{units(preview.totalUnits)} leave days</strong><span>Calculated from your current policy; the submitted request will be validated by the server.</span>{preview.days.map((day) => <small key={day.date}>{dateLabel(day.date)} · {units(day.units)} · Policy v{day.policyVersion}</small>)}</div>}
            </form>
          </section>
          <section className="leave-panel">
            <div className="leave-panel-heading"><div><h2>Leave history</h2><p>Immutable leave ledger activity</p></div></div>
            <LedgerTable entries={history} />
          </section>
        </>
      )}
    </section>
  )
}

function TeamLeave() {
  const [team, setTeam] = useState<LeaveTeamBalance[]>([])
  const [requests, setRequests] = useState<RequestItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    Promise.all([getTeamLeaveBalances(), getRequests()])
      .then(([balances, requestItems]) => {
        setTeam(balances)
        setRequests(requestItems)
      })
      .catch((caught: unknown) => setError(readableError(caught)))
      .finally(() => setLoading(false))
  }, [])

  const upcoming = requests.filter((item) =>
    item.requestTypeCode?.toUpperCase() === 'LEAVE' &&
    item.status.toLowerCase() === 'approved' &&
    !item.requestDetails?.cancellationOfRequestId &&
    (item.requestDetails?.startDate ?? '') >= today())
    .sort((left, right) =>
      (left.requestDetails?.startDate ?? '')
        .localeCompare(right.requestDetails?.startDate ?? ''))
  const teamRows = team.flatMap((person) =>
    person.balances.map((balance) => ({ person, balance })))

  return (
    <section className="leave-page">
      <header className="leave-page-heading"><div><p className="leave-eyebrow">TEAM OPERATIONS</p><h1>Team Leave</h1><p>Department-scoped leave visibility provided by your manager permissions.</p></div></header>
      {error && <div className="leave-alert" role="alert">{error}</div>}
      {loading ? <div className="leave-loading" role="status">Loading authorized team leave…</div> : (
        <>
          <section className="leave-panel">
            <div className="leave-panel-heading"><div><h2>Team balances</h2><p>Only employees in your authorized department are shown</p></div><span className="leave-status is-neutral">{team.length} team members</span></div>
            {teamRows.length === 0 ? <div className="leave-empty">No team leave balances are available.</div> : (
              <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Employee</th><th>Leave type</th><th>Earned</th><th>Used</th><th>Pending</th><th>Scheduled</th><th>Available</th></tr></thead><tbody>
                {teamRows.map(({ person, balance }) => <tr key={`${person.employeeUserId}-${balance.leaveTypeId}`}><td>{person.employeeName}</td><td>{balance.leaveTypeName}</td><td>{units(balance.earned)}</td><td>{units(balance.used)}</td><td>{units(balance.pending)}</td><td>{units(balance.scheduled)}</td><td><strong>{units(balance.available)}</strong></td></tr>)}
              </tbody></table></div>
            )}
          </section>
          <section className="leave-panel">
            <div className="leave-panel-heading"><div><h2>Upcoming approved leave</h2><p>Approval decisions remain in the existing workflow</p></div></div>
            {upcoming.length === 0 ? <div className="leave-empty">No upcoming approved leave is visible to your role.</div> : (
              <ul className="leave-upcoming-list">{upcoming.map((item) => <li key={item.id}><span className="leave-date-tile">{dateLabel(item.requestDetails?.startDate).split(' ')[0]}</span><div><strong>{item.createdByName ?? item.submittedBy ?? 'Team member'} · {item.requestDetails?.leaveType ?? 'Leave'}</strong><small>{dateLabel(item.requestDetails?.startDate)} – {dateLabel(item.requestDetails?.endDate)}</small></div><a className="leave-link" href={`#/assigned-to-me`}>Open workflow</a></li>)}</ul>
            )}
          </section>
        </>
      )}
    </section>
  )
}

function AdminLeaveManagement({
  users: initialUsers,
  departments: initialDepartments,
}: {
  users: User[]
  departments: Department[]
}) {
  const tabs: AdminTab[] = [
    'Overview', 'Leave Types', 'Policies', 'Employee Profiles',
    'Balances & Adjustments', 'Work Schedules', 'Holiday Calendars',
    'Payroll Calendars',
  ]
  const [tab, setTab] = useState<AdminTab>('Overview')
  const [types, setTypes] = useState<LeaveTypeInfo[]>([])
  const [policies, setPolicies] = useState<LeavePolicy[]>([])
  const [users, setUsers] = useState<User[]>(initialUsers)
  const [departments, setDepartments] = useState<Department[]>(initialDepartments)
  const [schedules, setSchedules] = useState<LeaveSchedule[]>([])
  const [holidayCalendars, setHolidayCalendars] = useState<LeaveHolidayCalendar[]>([])
  const [payrollCalendars, setPayrollCalendars] = useState<LeavePayrollCalendar[]>([])
  const [designations, setDesignations] = useState<LeaveDesignation[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const loadAdminData = useCallback(async (showLoading = true) => {
    if (showLoading) {
      setLoading(true)
      setError('')
    }
    try {
      const [typeData, policyData, scheduleData, holidayData, payrollData,
        designationData, userData, departmentData] = await fetchAdminLeavePageData(
          initialUsers,
          initialDepartments,
        )
      setTypes(typeData)
      setPolicies(policyData)
      setSchedules(scheduleData)
      setHolidayCalendars(holidayData)
      setPayrollCalendars(payrollData)
      setDesignations(designationData)
      setUsers(userData)
      setDepartments(departmentData)
    } catch (caught) {
      setError(readableError(caught))
    } finally {
      setLoading(false)
    }
  }, [initialDepartments, initialUsers])

  useEffect(() => {
    let current = true
    fetchAdminLeavePageData(initialUsers, initialDepartments)
      .then(([typeData, policyData, scheduleData, holidayData, payrollData,
        designationData, userData, departmentData]) => {
        if (!current) return
        setTypes(typeData)
        setPolicies(policyData)
        setSchedules(scheduleData)
        setHolidayCalendars(holidayData)
        setPayrollCalendars(payrollData)
        setDesignations(designationData)
        setUsers(userData)
        setDepartments(departmentData)
      })
      .catch((caught: unknown) => {
        if (current) setError(readableError(caught))
      })
      .finally(() => {
        if (current) setLoading(false)
      })
    return () => { current = false }
  }, [initialDepartments, initialUsers])

  async function perform(action: () => Promise<unknown>, message: string) {
    setError('')
    setNotice('')
    try {
      await action()
      setNotice(message)
      await loadAdminData()
    } catch (caught) {
      setError(readableError(caught))
    }
  }

  return (
    <section className="leave-page">
      <header className="leave-page-heading"><div><p className="leave-eyebrow">ADMINISTRATION</p><h1>Leave Management</h1><p>Configure the leave catalogue, policies, employee profiles, and calendars.</p></div></header>
      <nav className="leave-admin-tabs" aria-label="Leave administration sections">
        {tabs.map((item) => <button key={item} className={tab === item ? 'selected' : ''} onClick={() => setTab(item)}>{item}</button>)}
      </nav>
      {error && <div className="leave-alert" role="alert">{error}</div>}
      {notice && <div className="leave-notice" role="status">{notice}</div>}
      {loading ? <div className="leave-loading" role="status">Loading leave administration…</div> : (
        <>
          {tab === 'Overview' && <AdminOverview types={types} policies={policies} users={users} />}
          {tab === 'Leave Types' && <LeaveTypesAdmin types={types} perform={perform} />}
          {tab === 'Policies' && <PoliciesAdmin types={types} policies={policies} users={users} departments={departments} designations={designations} schedules={schedules} calendars={holidayCalendars} payroll={payrollCalendars} perform={perform} />}
          {tab === 'Employee Profiles' && <ProfilesAdmin users={users} designations={designations} perform={perform} />}
          {tab === 'Balances & Adjustments' && <AdjustmentsAdmin users={users} types={types} perform={perform} />}
          {tab === 'Work Schedules' && <SchedulesAdmin schedules={schedules} perform={perform} />}
          {tab === 'Holiday Calendars' && <HolidaysAdmin calendars={holidayCalendars} perform={perform} />}
          {tab === 'Payroll Calendars' && <PayrollAdmin calendars={payrollCalendars} perform={perform} />}
        </>
      )}
    </section>
  )
}

function AdminOverview({ types, policies, users }: { types: LeaveTypeInfo[]; policies: LeavePolicy[]; users: User[] }) {
  const [person, setPerson] = useState('')
  const [balances, setBalances] = useState<LeaveBalance[]>([])
  const [error, setError] = useState('')
  return (
    <>
      <div className="leave-summary-grid leave-summary-grid-admin">
        {[['Enabled leave types', types.filter((item) => item.isEnabled).length], ['Published policies', policies.filter((item) => item.status === 'Published').length], ['Active employees', users.filter((user) => user.isActive && user.role.toLowerCase() === 'employee').length]].map(([label, value]) => <article className="leave-summary-card" key={label}><span>{label}</span><strong>{value}</strong><small>Current catalogue</small></article>)}
      </div>
      <section className="leave-panel">
        <div className="leave-panel-heading"><div><h2>Employee balance lookup</h2><p>Balances reflect the append-only ledger through today</p></div></div>
        <div className="leave-inline-form"><label>Employee<select value={person} onChange={(event) => { setPerson(event.target.value); setBalances([]); setError('') }}><option value="">Select employee</option>{users.map((user) => <option key={user.id} value={user.id}>{user.fullName}</option>)}</select></label><button className="leave-button leave-button-primary" disabled={!person} onClick={() => { setError(''); void getEmployeeLeaveBalances(Number(person)).then(setBalances).catch((caught: unknown) => setError(readableError(caught))) }}>View balances</button></div>
        {error && <div className="leave-alert" role="alert">{error}</div>}
        {balances.length > 0 && <BalanceTable balances={balances} />}
      </section>
      <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Policy catalogue</h2><p>Published policy versions are read-only</p></div></div><div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Policy</th><th>Version</th><th>Scope</th><th>Effective</th><th>Status</th></tr></thead><tbody>{policies.map((policy) => <tr key={policy.id}><td>{policy.name}<small>{policy.code}</small></td><td>{policy.version}</td><td>{policy.scope}</td><td>{dateLabel(policy.effectiveFrom)}{policy.effectiveTo ? ` – ${dateLabel(policy.effectiveTo)}` : ' onward'}</td><td><span className={`leave-status ${statusClass(policy.status)}`}>{policy.status}</span></td></tr>)}</tbody></table></div></section>
    </>
  )
}

function LeaveTypesAdmin({ types, perform }: { types: LeaveTypeInfo[]; perform: (action: () => Promise<unknown>, message: string) => Promise<void> }) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [paid, setPaid] = useState(true)
  return <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Leave types</h2><p>Catalogue entries only; entitlements are configured in policy versions.</p></div></div>
    <form className="leave-inline-form" onSubmit={(event) => { event.preventDefault(); void perform(() => createAdminLeaveType({ code, displayName: name, isPaid: paid }), 'Leave type created.') }}>
      <label>Code<input required maxLength={50} value={code} onChange={(event) => setCode(event.target.value)} /></label><label>Display name<input required maxLength={100} value={name} onChange={(event) => setName(event.target.value)} /></label><label className="leave-check"><input type="checkbox" checked={paid} onChange={(event) => setPaid(event.target.checked)} /> Paid leave</label><button className="leave-button leave-button-primary">Add leave type</button>
    </form>
    <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Code</th><th>Name</th><th>Paid</th><th>Status</th><th>Action</th></tr></thead><tbody>{types.map((item) => <tr key={item.id}><td>{item.code}</td><td>{item.displayName}</td><td>{item.isPaid ? 'Yes' : 'No'}</td><td><span className={`leave-status ${item.isEnabled ? 'is-positive' : 'is-neutral'}`}>{item.isEnabled ? 'Enabled' : 'Disabled'}</span></td><td><button className="leave-button leave-button-small" disabled={!item.isEnabled} onClick={() => void perform(() => updateAdminLeaveType(item.id, { displayName: item.displayName, isPaid: item.isPaid, isEnabled: false }), 'Leave type disabled.')}>Disable</button></td></tr>)}</tbody></table></div>
  </section>
}

function PoliciesAdmin({
  types, policies, users, departments, designations, schedules, calendars, payroll, perform,
}: {
  types: LeaveTypeInfo[]
  policies: LeavePolicy[]
  users: User[]
  departments: Department[]
  designations: LeaveDesignation[]
  schedules: LeaveSchedule[]
  calendars: LeaveHolidayCalendar[]
  payroll: LeavePayrollCalendar[]
  perform: (action: () => Promise<unknown>, message: string) => Promise<void>
}) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [scope, setScope] = useState<LeavePolicyDraftInput['scope']>('Company')
  const [target, setTarget] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState(today())
  const [dayMethod, setDayMethod] = useState<'CalendarDays' | 'WorkingDays'>('WorkingDays')
  const [schedule, setSchedule] = useState('')
  const [calendar, setCalendar] = useState('')
  const [allowHalf, setAllowHalf] = useState(true)
  const [accrual, setAccrual] = useState<'Annually' | 'Monthly' | 'PerPayPeriod' | ''>('')
  const [payCalendar, setPayCalendar] = useState('')
  const [carry, setCarry] = useState(false)
  const [carryCap, setCarryCap] = useState('')
  const [expiryMonths, setExpiryMonths] = useState('')
  const [negative, setNegative] = useState(false)
  const [entitlements, setEntitlements] = useState<Record<number, string>>({})
  const [assignmentEmployee, setAssignmentEmployee] = useState('')
  const [assignmentPolicy, setAssignmentPolicy] = useState('')
  const [assignmentDate, setAssignmentDate] = useState(today())

  function buildInput(): LeavePolicyDraftInput {
    const targetId = target ? Number(target) : null
    return {
      code, name, version: 1, scope,
      departmentId: scope === 'Department' ? targetId : null,
      designationId: scope === 'Designation' ? targetId : null,
      employmentType: scope === 'EmploymentType' ? target : null,
      effectiveFrom, effectiveTo: null,
      dayCalculationMethod: dayMethod,
      leaveWorkScheduleId: dayMethod === 'WorkingDays' && schedule ? Number(schedule) : null,
      leaveHolidayCalendarId: calendar ? Number(calendar) : null,
      allowsHalfDays: allowHalf, halfDayUnits: allowHalf ? 0.5 : null,
      isAccrualEnabled: Boolean(accrual),
      accrualFrequency: accrual || null,
      allowsCarryForward: carry,
      carryForwardCap: carry ? Number(carryCap) : null,
      carryForwardExpiryMonths: carry ? Number(expiryMonths) : null,
      probationPeriodDays: null,
      allowsLeaveDuringProbation: true,
      allowsNegativeBalance: negative,
      leavePayrollCalendarId: accrual === 'PerPayPeriod' && payCalendar ? Number(payCalendar) : null,
      entitlements: types.filter((item) => item.isEnabled)
        .map((item) => ({ leaveTypeId: item.id, annualEntitlement: Number(entitlements[item.id] ?? 0) })),
    }
  }

  return <>
    <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Create draft policy</h2><p>Define scope and configuration. Allowance values are provided explicitly for each enabled leave type.</p></div></div>
      <form className="leave-form-grid" onSubmit={(event) => { event.preventDefault(); void perform(() => createAdminLeavePolicy(buildInput()), 'Draft policy created.') }}>
        <label>Policy code<input required maxLength={50} value={code} onChange={(event) => setCode(event.target.value)} /></label><label>Policy name<input required maxLength={120} value={name} onChange={(event) => setName(event.target.value)} /></label>
        <label>Scope<select value={scope} onChange={(event) => { setScope(event.target.value as LeavePolicyDraftInput['scope']); setTarget('') }}><option value="Company">Company default</option><option value="Department">Department</option><option value="Designation">Designation</option><option value="EmploymentType">Employment type</option></select></label>
        {scope !== 'Company' && <label>Scope target{scope === 'Department' ? <select required value={target} onChange={(event) => setTarget(event.target.value)}><option value="">Select department</option>{departments.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select> : scope === 'Designation' ? <select required value={target} onChange={(event) => setTarget(event.target.value)}><option value="">Select designation</option>{designations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select> : <select required value={target} onChange={(event) => setTarget(event.target.value)}><option value="">Select employment type</option>{['FullTime', 'PartTime', 'Contract'].map((item) => <option key={item}>{item}</option>)}</select>}</label>}
        <label>Effective from<input type="date" required value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} /></label>
        <label>Day calculation<select value={dayMethod} onChange={(event) => setDayMethod(event.target.value as typeof dayMethod)}><option value="WorkingDays">Working days</option><option value="CalendarDays">Calendar days</option></select></label>
        {dayMethod === 'WorkingDays' && <label>Work schedule<select required value={schedule} onChange={(event) => setSchedule(event.target.value)}><option value="">Select schedule</option>{schedules.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        <label>Holiday calendar<select value={calendar} onChange={(event) => setCalendar(event.target.value)}><option value="">No holiday calendar</option>{calendars.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="leave-check"><input type="checkbox" checked={allowHalf} onChange={(event) => setAllowHalf(event.target.checked)} /> Allow half days (0.5 units)</label>
        <label>Accrual frequency<select value={accrual} onChange={(event) => setAccrual(event.target.value as typeof accrual)}><option value="">No accrual</option><option value="Annually">Annually</option><option value="Monthly">Monthly</option><option value="PerPayPeriod">Per pay period</option></select></label>
        {accrual === 'PerPayPeriod' && <label>Payroll calendar<select required value={payCalendar} onChange={(event) => setPayCalendar(event.target.value)}><option value="">Select calendar</option>{payroll.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
        <label className="leave-check"><input type="checkbox" checked={carry} onChange={(event) => setCarry(event.target.checked)} /> Allow carry-forward</label>
        {carry && <><label>Carry-forward cap<input required type="number" min="0" step="0.01" value={carryCap} onChange={(event) => setCarryCap(event.target.value)} /></label><label>Expiry months<input required type="number" min="1" max="120" value={expiryMonths} onChange={(event) => setExpiryMonths(event.target.value)} /></label></>}
        <label className="leave-check"><input type="checkbox" checked={negative} onChange={(event) => setNegative(event.target.checked)} /> Allow negative balance</label>
        <fieldset className="leave-entitlement-fieldset leave-form-wide"><legend>Annual entitlement per enabled type</legend>{types.filter((item) => item.isEnabled).map((item) => <label key={item.id}>{item.displayName}<input required type="number" min="0" step="0.01" value={entitlements[item.id] ?? ''} onChange={(event) => setEntitlements((current) => ({ ...current, [item.id]: event.target.value }))} /></label>)}</fieldset>
        <div className="leave-form-actions leave-form-wide"><button className="leave-button leave-button-primary">Save draft policy</button></div>
      </form>
    </section>
    <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Policy versions</h2><p>Published policies cannot be edited; publish future versions instead.</p></div></div><div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Policy</th><th>Version</th><th>Scope</th><th>Effective from</th><th>Status</th><th>Action</th></tr></thead><tbody>{policies.map((policy) => <tr key={policy.id}><td>{policy.name}<small>{policy.code}</small></td><td>{policy.version}</td><td>{policy.scope}</td><td>{dateLabel(policy.effectiveFrom)}</td><td><span className={`leave-status ${statusClass(policy.status)}`}>{policy.status}</span></td><td>{policy.status === 'Draft' && <button className="leave-button leave-button-small" onClick={() => void perform(() => publishAdminLeavePolicy(policy.id), 'Policy published.')}>Publish</button>}</td></tr>)}</tbody></table></div></section>
    <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Employee policy assignment</h2><p>Explicit employee assignments have the highest resolution precedence.</p></div></div><form className="leave-inline-form" onSubmit={(event) => { event.preventDefault(); void perform(() => assignLeavePolicy({ employeeUserId: Number(assignmentEmployee), leavePolicyId: Number(assignmentPolicy), effectiveFrom: assignmentDate, effectiveTo: null }), 'Policy assignment created.') }}><label>Employee<select required value={assignmentEmployee} onChange={(event) => setAssignmentEmployee(event.target.value)}><option value="">Select employee</option>{users.filter((user) => user.role.toLowerCase() === 'employee').map((user) => <option key={user.id} value={user.id}>{user.fullName}</option>)}</select></label><label>Published policy<select required value={assignmentPolicy} onChange={(event) => setAssignmentPolicy(event.target.value)}><option value="">Select policy</option>{policies.filter((policy) => policy.status === 'Published').map((policy) => <option key={policy.id} value={policy.id}>{policy.name} · v{policy.version}</option>)}</select></label><label>Effective from<input required type="date" value={assignmentDate} onChange={(event) => setAssignmentDate(event.target.value)} /></label><button className="leave-button leave-button-primary">Assign policy</button></form></section>
  </>
}

function ProfilesAdmin({ users, designations, perform }: { users: User[]; designations: LeaveDesignation[]; perform: (action: () => Promise<unknown>, message: string) => Promise<void> }) {
  const [employee, setEmployee] = useState('')
  const [designation, setDesignation] = useState('')
  const [designationCode, setDesignationCode] = useState('')
  const [designationName, setDesignationName] = useState('')
  const [employmentType, setEmploymentType] = useState('')
  const [hireDate, setHireDate] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState(today())
  const [profiles, setProfiles] = useState<EmployeeLeaveMetadata[]>([])
  const [error, setError] = useState('')
  async function loadProfiles(id: string) {
    setEmployee(id)
    setProfiles([])
    if (!id) return
    try { setProfiles(await getEmployeeLeaveMetadata(Number(id))) } catch (caught) { setError(readableError(caught)) }
  }
  return <>
    <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Employment profiles</h2><p>Changes are effective-dated; previous profile history is preserved.</p></div></div>
    <form className="leave-form-grid" onSubmit={(event) => { event.preventDefault(); void perform(() => saveEmployeeLeaveMetadata({ employeeUserId: Number(employee), designationId: designation ? Number(designation) : null, employmentType: employmentType || null, hireDate: hireDate || null, effectiveFrom, effectiveTo: null }), 'Employment profile saved.') }}>
      <label>Employee<select required value={employee} onChange={(event) => void loadProfiles(event.target.value)}><option value="">Select employee</option>{users.filter((user) => user.role.toLowerCase() === 'employee').map((user) => <option key={user.id} value={user.id}>{user.fullName}</option>)}</select></label>
      <label>Designation<select value={designation} onChange={(event) => setDesignation(event.target.value)}><option value="">Not specified</option>{designations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select>{designations.length === 0 && <small>No active designations configured yet.</small>}</label>
      <label>Employment type<select value={employmentType} onChange={(event) => setEmploymentType(event.target.value)}><option value="">Not specified</option>{['FullTime', 'PartTime', 'Contract'].map((item) => <option key={item}>{item}</option>)}</select></label>
      <label>Hire date<input type="date" value={hireDate} onChange={(event) => setHireDate(event.target.value)} /></label>
      <label>Effective from<input required type="date" value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} /></label>
      <div className="leave-form-actions"><button className="leave-button leave-button-primary" disabled={!employee}>Save profile change</button></div>
    </form>
    {error && <div className="leave-alert" role="alert">{error}</div>}
    {profiles.length > 0 && <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Effective</th><th>Designation</th><th>Employment type</th><th>Hire date</th></tr></thead><tbody>{profiles.map((profile, index) => <tr key={`${profile.effectiveFrom}-${index}`}><td>{dateLabel(profile.effectiveFrom)}{profile.effectiveTo ? ` – ${dateLabel(profile.effectiveTo)}` : ' onward'}</td><td>{profile.designationName ?? 'Not specified'}</td><td>{profile.employmentType ?? 'Not specified'}</td><td>{dateLabel(profile.hireDate)}</td></tr>)}</tbody></table></div>}
    </section>
    <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Designation catalogue</h2><p>Only active designations are available to policy and employee profile forms.</p></div></div>
      <form className="leave-inline-form" onSubmit={(event) => { event.preventDefault(); void perform(() => createLeaveDesignation({ code: designationCode, name: designationName }), 'Designation created.') }}>
        <label>Code<input required maxLength={50} value={designationCode} onChange={(event) => setDesignationCode(event.target.value)} /></label>
        <label>Name<input required maxLength={100} value={designationName} onChange={(event) => setDesignationName(event.target.value)} /></label>
        <button className="leave-button leave-button-primary">Add designation</button>
      </form>
      <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Code</th><th>Name</th><th>Status</th></tr></thead><tbody>{designations.length === 0 ? <tr><td colSpan={3}>No active designations yet.</td></tr> : designations.map((item) => <tr key={item.id}><td>{item.code}</td><td>{item.name}</td><td><span className="leave-status is-positive">Active</span></td></tr>)}</tbody></table></div>
    </section>
  </>
}

function AdjustmentsAdmin({ users, types, perform }: { users: User[]; types: LeaveTypeInfo[]; perform: (action: () => Promise<unknown>, message: string) => Promise<void> }) {
  const [employee, setEmployee] = useState('')
  const [leaveType, setLeaveType] = useState('')
  const [balances, setBalances] = useState<LeaveBalance[]>([])
  const [history, setHistory] = useState<LeaveLedgerItem[]>([])
  const [unitsValue, setUnitsValue] = useState('')
  const [effectiveDate, setEffectiveDate] = useState(today())
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  async function selectEmployee(id: string) {
    setEmployee(id)
    setBalances([])
    setHistory([])
    if (!id) return
    try {
      const [balanceRows, auditRows] = await Promise.all([
        getEmployeeLeaveBalances(Number(id)),
        getLeaveAdjustmentAudit(Number(id)),
      ])
      setBalances(balanceRows)
      setHistory(auditRows)
    } catch (caught) { setError(readableError(caught)) }
  }
  return <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Employee balances and adjustments</h2><p>Every adjustment is attributed to the acting Admin and written to the immutable ledger.</p></div></div>
    <div className="leave-inline-form"><label>Employee<select value={employee} onChange={(event) => void selectEmployee(event.target.value)}><option value="">Select employee</option>{users.filter((user) => user.role.toLowerCase() === 'employee').map((user) => <option key={user.id} value={user.id}>{user.fullName}</option>)}</select></label></div>
    {error && <div className="leave-alert" role="alert">{error}</div>}
    {balances.length > 0 && <BalanceTable balances={balances} />}
    <form className="leave-form-grid leave-adjustment-form" onSubmit={(event) => { event.preventDefault(); const key = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`; void perform(() => createLeaveAdjustment({ employeeUserId: Number(employee), leaveTypeId: Number(leaveType), units: Number(unitsValue), effectiveDate, reason, idempotencyKey: key }), 'Balance adjustment posted.') }}>
      <label>Leave type<select required value={leaveType} onChange={(event) => setLeaveType(event.target.value)}><option value="">Select type</option>{types.map((item) => <option key={item.id} value={item.id}>{item.displayName}</option>)}</select></label>
      <label>Units (+ or −)<input required type="number" step="0.01" min="-9999999.99" max="9999999.99" value={unitsValue} onChange={(event) => setUnitsValue(event.target.value)} /></label>
      <label>Effective date<input required type="date" value={effectiveDate} onChange={(event) => setEffectiveDate(event.target.value)} /></label>
      <label>Reason<input required maxLength={1000} value={reason} onChange={(event) => setReason(event.target.value)} /></label>
      <div className="leave-form-actions"><button className="leave-button leave-button-primary" disabled={!employee}>Post adjustment</button></div>
    </form>
    <h3 className="leave-subheading">Adjustment audit history</h3><LedgerTable entries={history} />
  </section>
}

function SchedulesAdmin({ schedules, perform }: { schedules: LeaveSchedule[]; perform: (action: () => Promise<unknown>, message: string) => Promise<void> }) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const weekdays = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
  const [workingDays, setWorkingDays] = useState([false, true, true, true, true, true, false])
  const [hours, setHours] = useState('8')
  return <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Work schedules</h2><p>Weekly schedules define working days and standard hours.</p></div></div>
    <form className="leave-form-grid" onSubmit={(event) => { event.preventDefault(); void perform(() => createLeaveWorkSchedule({ code, name, days: weekdays.map((_, dayOfWeek) => ({ dayOfWeek, isWorkingDay: workingDays[dayOfWeek], standardHours: workingDays[dayOfWeek] ? Number(hours) : 0 })) }), 'Work schedule created.') }}>
      <label>Code<input required maxLength={50} value={code} onChange={(event) => setCode(event.target.value)} /></label><label>Name<input required maxLength={100} value={name} onChange={(event) => setName(event.target.value)} /></label><label>Hours per working day<input required type="number" min="0.01" max="24" step="0.25" value={hours} onChange={(event) => setHours(event.target.value)} /></label>
      <fieldset className="leave-entitlement-fieldset leave-form-wide"><legend>Working days</legend><div className="leave-weekday-list">{weekdays.map((day, index) => <label className="leave-check" key={day}><input type="checkbox" checked={workingDays[index]} onChange={(event) => setWorkingDays((current) => current.map((value, position) => position === index ? event.target.checked : value))} />{day}</label>)}</div></fieldset>
      <div className="leave-form-actions"><button className="leave-button leave-button-primary">Create schedule</button></div>
    </form>
    <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Code</th><th>Schedule</th><th>Working days</th><th>Status</th></tr></thead><tbody>{schedules.length === 0 ? <tr><td colSpan={4}>No work schedules yet. Create one above to use in working-day leave policies.</td></tr> : schedules.map((item) => <tr key={item.id}><td>{item.code}</td><td>{item.name}</td><td>{item.days.filter((day) => day.isWorkingDay).map((day) => weekdays[day.dayOfWeek].slice(0, 3)).join(', ')}</td><td><span className={`leave-status ${item.isEnabled ? 'is-positive' : 'is-neutral'}`}>{item.isEnabled ? 'Enabled' : 'Disabled'}</span></td></tr>)}</tbody></table></div>
  </section>
}

function HolidaysAdmin({ calendars, perform }: { calendars: LeaveHolidayCalendar[]; perform: (action: () => Promise<unknown>, message: string) => Promise<void> }) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [region, setRegion] = useState('')
  const [selected, setSelected] = useState('')
  const [holidayDate, setHolidayDate] = useState('')
  const [holidayName, setHolidayName] = useState('')
  return <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Holiday calendars</h2><p>Configure public holidays for working-day policies.</p></div></div>
    <form className="leave-inline-form" onSubmit={(event) => { event.preventDefault(); void perform(() => createLeaveHolidayCalendar({ code, name, region: region || null }), 'Holiday calendar created.') }}><label>Code<input required value={code} maxLength={50} onChange={(event) => setCode(event.target.value)} /></label><label>Name<input required value={name} maxLength={100} onChange={(event) => setName(event.target.value)} /></label><label>Region<input value={region} maxLength={100} onChange={(event) => setRegion(event.target.value)} /></label><button className="leave-button leave-button-primary">Create calendar</button></form>
    <form className="leave-inline-form leave-form-divider" onSubmit={(event) => { event.preventDefault(); void perform(() => addLeaveHoliday(Number(selected), { date: holidayDate, name: holidayName }), 'Public holiday added.') }}><label>Calendar<select required value={selected} onChange={(event) => setSelected(event.target.value)}><option value="">Select calendar</option>{calendars.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label>Date<input required type="date" value={holidayDate} onChange={(event) => setHolidayDate(event.target.value)} /></label><label>Holiday name<input required maxLength={150} value={holidayName} onChange={(event) => setHolidayName(event.target.value)} /></label><button className="leave-button leave-button-primary">Add holiday</button></form>
    {calendars.map((calendar) => <div className="leave-calendar-list" key={calendar.id}><h3>{calendar.name}{calendar.region ? ` · ${calendar.region}` : ''}</h3>{calendar.holidays.length ? <ul>{calendar.holidays.sort((a, b) => a.date.localeCompare(b.date)).map((holiday) => <li key={holiday.id}><span>{dateLabel(holiday.date)}</span><strong>{holiday.name}</strong></li>)}</ul> : <p className="leave-muted">No holidays configured.</p>}</div>)}
  </section>
}

function PayrollAdmin({ calendars, perform }: { calendars: LeavePayrollCalendar[]; perform: (action: () => Promise<unknown>, message: string) => Promise<void> }) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [timeZoneId, setTimeZoneId] = useState('UTC')
  const [periodCount, setPeriodCount] = useState('26')
  const [selected, setSelected] = useState('')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')
  const [postingDate, setPostingDate] = useState('')
  const [entitlementYear, setEntitlementYear] = useState(String(CURRENT_YEAR))
  const [periodNumber, setPeriodNumber] = useState('1')
  return <section className="leave-panel"><div className="leave-panel-heading"><div><h2>Payroll calendars and pay periods</h2><p>Each period is non-overlapping and has a posting date.</p></div></div>
    <form className="leave-form-grid" onSubmit={(event) => { event.preventDefault(); void perform(() => createLeavePayrollCalendar({ code, name, timeZoneId, expectedPeriodsInEntitlementYear: Number(periodCount) }), 'Payroll calendar created.') }}><label>Code<input required maxLength={50} value={code} onChange={(event) => setCode(event.target.value)} /></label><label>Name<input required maxLength={100} value={name} onChange={(event) => setName(event.target.value)} /></label><label>Business time zone<input required maxLength={100} value={timeZoneId} onChange={(event) => setTimeZoneId(event.target.value)} /></label><label>Periods per entitlement year<input required type="number" min="1" max="366" value={periodCount} onChange={(event) => setPeriodCount(event.target.value)} /></label><div className="leave-form-actions"><button className="leave-button leave-button-primary">Create payroll calendar</button></div></form>
    <form className="leave-form-grid leave-form-divider" onSubmit={(event) => { event.preventDefault(); void perform(() => addLeavePayPeriod(Number(selected), { startDate, endDate, postingDate, entitlementYear: Number(entitlementYear), periodNumber: Number(periodNumber) }), 'Pay period added.') }}><label>Calendar<select required value={selected} onChange={(event) => setSelected(event.target.value)}><option value="">Select calendar</option>{calendars.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label>Start<input required type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} /></label><label>End<input required type="date" min={startDate || undefined} value={endDate} onChange={(event) => setEndDate(event.target.value)} /></label><label>Posting date<input required type="date" min={endDate || startDate || undefined} value={postingDate} onChange={(event) => setPostingDate(event.target.value)} /></label><label>Entitlement year<input required type="number" min="1" value={entitlementYear} onChange={(event) => setEntitlementYear(event.target.value)} /></label><label>Period number<input required type="number" min="1" value={periodNumber} onChange={(event) => setPeriodNumber(event.target.value)} /></label><div className="leave-form-actions"><button className="leave-button leave-button-primary">Add pay period</button></div></form>
    {calendars.map((item) => <div className="leave-calendar-list" key={item.id}><h3>{item.name} <span className="leave-muted">· {item.timeZoneId}</span></h3>{item.periods.length ? <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Year / period</th><th>Period</th><th>Posting date</th></tr></thead><tbody>{[...item.periods].sort((a, b) => a.startDate.localeCompare(b.startDate)).map((period) => <tr key={period.id}><td>{period.entitlementYear} / {period.periodNumber}</td><td>{dateLabel(period.startDate)} – {dateLabel(period.endDate)}</td><td>{dateLabel(period.postingDate)}</td></tr>)}</tbody></table></div> : <p className="leave-muted">No pay periods configured.</p>}</div>)}
  </section>
}

function BalanceTable({ balances }: { balances: LeaveBalance[] }) {
  return <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Leave type</th><th>Earned</th><th>Adjustments</th><th>Carry forward</th><th>Used</th><th>Pending</th><th>Scheduled</th><th>Available</th></tr></thead><tbody>{balances.map((item) => <tr key={item.leaveTypeId}><td>{item.leaveTypeName}</td><td>{units(item.earned)}</td><td>{units(item.adjustments)}</td><td>{units(item.carryForward)}</td><td>{units(item.used)}</td><td>{units(item.pending)}</td><td>{units(item.scheduled)}</td><td><strong>{units(item.available)}</strong></td></tr>)}</tbody></table></div>
}

function LedgerTable({ entries }: { entries: LeaveLedgerItem[] }) {
  if (!entries.length) return <div className="leave-empty">No ledger entries to display.</div>
  return <div className="leave-table-wrap"><table className="leave-table"><thead><tr><th>Date</th><th>Leave type</th><th>Activity</th><th>Units</th><th>Expires</th><th>Reason / request</th></tr></thead><tbody>{entries.map((entry) => <tr key={entry.id}><td>{dateLabel(entry.effectiveDate)}</td><td>{entry.leaveTypeName}</td><td><span className={`leave-status ${statusClass(entry.entryType)}`}>{entry.entryType.replaceAll(/([a-z])([A-Z])/g, '$1 $2')}</span></td><td>{units(entry.units)}</td><td>{dateLabel(entry.expiresOn)}</td><td>{entry.reason ?? (entry.requestId ? `Request #${entry.requestId}` : '—')}</td></tr>)}</tbody></table></div>
}
