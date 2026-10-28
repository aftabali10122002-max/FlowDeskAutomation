import { useEffect, useMemo, useState } from 'react'
import {
  getApprovalHistoryReport,
  getLeaveReport,
  getReportFilterOptions,
  getRequestReport,
  getWorkflowPerformanceReport,
  downloadReportExport,
} from './api'
import type {
  ApprovalHistoryReportRow,
  LeaveReportRow,
  ReportExportFormat,
  ReportFilterOptions,
  ReportPage,
  ReportQuery,
  RequestReportRow,
  WorkflowPerformanceReportRow,
} from './api'
import './Reports.css'

type ReportTab = 'requests' | 'approvals' | 'workflows' | 'leave'
type ReportRole = 'Admin' | 'Manager' | 'Employee'
type SortDirection = 'asc' | 'desc'

const tabs: { id: ReportTab; label: string }[] = [
  { id: 'requests', label: 'Request Reports' },
  { id: 'approvals', label: 'Approval History' },
  { id: 'workflows', label: 'Workflow Performance' },
  { id: 'leave', label: 'Leave Reports' },
]
const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' })
const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
})
const numberFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 })

function isoDate(date: Date) {
  return date.toISOString().slice(0, 10)
}

function getInitialDateRange() {
  const today = isoDate(new Date())
  const from = new Date(`${today}T00:00:00Z`)
  from.setUTCDate(from.getUTCDate() - 29)
  return { from: isoDate(from), to: today }
}

function formatDate(value: string) {
  const date = new Date(`${value.slice(0, 10)}T00:00:00`)
  return Number.isNaN(date.getTime()) ? '—' : dateFormat.format(date)
}

function formatDateTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : dateTimeFormat.format(date)
}

function formatUnits(value: number | null) {
  return value == null ? '—' : numberFormat.format(value)
}

export function Reports({ role }: { role: ReportRole }) {
  const [initialRange] = useState(getInitialDateRange)
  const { from: initialFrom, to: today } = initialRange
  const [tab, setTab] = useState<ReportTab>('requests')
  const [from, setFrom] = useState(initialFrom)
  const [to, setTo] = useState(today)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [requestTypeId, setRequestTypeId] = useState('')
  const [status, setStatus] = useState('')
  const [priority, setPriority] = useState('')
  const [employeeUserId, setEmployeeUserId] = useState('')
  const [managerUserId, setManagerUserId] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [sortBy, setSortBy] = useState('createdAtUtc')
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc')
  const [options, setOptions] = useState<ReportFilterOptions | null>(null)
  const [optionsError, setOptionsError] = useState('')
  const [requestPage, setRequestPage] = useState<ReportPage<RequestReportRow> | null>(null)
  const [approvalPage, setApprovalPage] = useState<ReportPage<ApprovalHistoryReportRow> | null>(null)
  const [workflowPage, setWorkflowPage] = useState<ReportPage<WorkflowPerformanceReportRow> | null>(null)
  const [leavePage, setLeavePage] = useState<ReportPage<LeaveReportRow> | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [exportingFormat, setExportingFormat] = useState<ReportExportFormat | null>(null)
  const [exportError, setExportError] = useState('')

  useEffect(() => {
    let active = true
    void getReportFilterOptions()
      .then((result) => {
        if (active) {
          setOptions(result)
          setOptionsError('')
        }
      })
      .catch((caught: unknown) => {
        if (active) {
          setOptionsError(caught instanceof Error
            ? caught.message
            : 'Could not load authorized report filter options.')
        }
      })
    return () => { active = false }
  }, [])

  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedSearch(search.trim()), 250)
    return () => window.clearTimeout(timeout)
  }, [search])

  const query = useMemo<ReportQuery>(() => ({
    from,
    to,
    page,
    pageSize,
    sortBy,
    sortDirection,
    search: debouncedSearch,
    ...(departmentId ? { departmentId: Number(departmentId) } : {}),
    ...(requestTypeId ? { requestTypeId: Number(requestTypeId) } : {}),
    ...(status ? { status } : {}),
    ...(priority ? { priority } : {}),
    ...(employeeUserId ? { employeeUserId: Number(employeeUserId) } : {}),
    ...(managerUserId ? { managerUserId: Number(managerUserId) } : {}),
  }), [
    from,
    to,
    page,
    pageSize,
    sortBy,
    sortDirection,
    debouncedSearch,
    departmentId,
    requestTypeId,
    status,
    priority,
    employeeUserId,
    managerUserId,
  ])

  useEffect(() => {
    let active = true
    const load = async () => {
      setLoading(true)
      try {
        switch (tab) {
          case 'requests':
            setRequestPage(await getRequestReport(query))
            break
          case 'approvals':
            setApprovalPage(await getApprovalHistoryReport(query))
            break
          case 'workflows':
            setWorkflowPage(await getWorkflowPerformanceReport(query))
            break
          case 'leave':
            setLeavePage(await getLeaveReport(query))
            break
        }
        if (active) setError('')
      } catch (caught) {
        if (active) {
          setError(caught instanceof Error
            ? caught.message
            : 'The report could not be loaded. Please try again.')
        }
      } finally {
        if (active) setLoading(false)
      }
    }
    void load()
    return () => { active = false }
  }, [tab, query])

  const activePage = tab === 'requests'
    ? requestPage
    : tab === 'approvals'
      ? approvalPage
      : tab === 'workflows'
        ? workflowPage
        : leavePage
  const totalCount = activePage?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  function changeFilter(action: () => void) {
    action()
    setPage(1)
  }

  function sort(field: string) {
    if (sortBy === field) {
      setSortDirection((current) => current === 'asc' ? 'desc' : 'asc')
    } else {
      setSortBy(field)
      setSortDirection('asc')
    }
    setPage(1)
  }

  async function exportReport(format: ReportExportFormat) {
    const report = tab === 'approvals'
      ? 'approval-history'
      : tab === 'workflows'
        ? 'workflow-performance'
        : tab
    setExportingFormat(format)
    setExportError('')
    try {
      const { blob, fileName } = await downloadReportExport(
        report,
        { ...query, search: search.trim() },
        format,
      )
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = fileName
      document.body.appendChild(link)
      link.click()
      link.remove()
      URL.revokeObjectURL(url)
    } catch (caught) {
      setExportError(caught instanceof Error
        ? caught.message
        : 'The report export failed. Please try again.')
    } finally {
      setExportingFormat(null)
    }
  }

  function renderRows() {
    if (tab === 'requests' && requestPage) {
      return requestPage.items.map((row) => <tr key={row.requestId}>
        <td><strong>{row.title}</strong><small>#{row.requestId}</small></td>
        <td>{row.requestTypeName ?? 'Unspecified'}</td>
        <td><span className={`report-status report-status-${row.status.toLowerCase()}`}>{row.status}</span></td>
        <td>{row.priority ?? 'Unspecified'}</td>
        <td>{row.employeeName ?? 'Legacy request'}</td>
        <td>{row.departmentName ?? 'Unassigned'}</td>
        <td>{formatDateTime(row.createdAtUtc)}</td>
      </tr>)
    }
    if (tab === 'approvals' && approvalPage) {
      return approvalPage.items.map((row) => <tr key={`${row.source}-${row.recordId}`}>
        <td><strong>{row.requestTitle}</strong><small>#{row.requestId} · {row.source}</small></td>
        <td><span className={`report-status report-status-${row.decision.toLowerCase()}`}>{row.decision}</span></td>
        <td>{row.managerName}</td>
        <td>{row.stageSequence == null ? 'Manual' : `Stage ${row.stageSequence}`}</td>
        <td>{row.employeeName ?? 'Legacy request'}</td>
        <td>{row.departmentName ?? 'Unassigned'}</td>
        <td>{formatDateTime(row.decidedAtUtc)}</td>
      </tr>)
    }
    if (tab === 'workflows' && workflowPage) {
      return workflowPage.items.map((row) => <tr key={row.requestId}>
        <td><strong>{row.requestTitle}</strong><small>#{row.requestId}</small></td>
        <td>{row.workflowName} <small>v{row.workflowVersion}</small></td>
        <td>{row.workflowStatus}</td>
        <td>{row.stageCount} total · {row.approvedStageCount} approved · {row.activeStageCount} active</td>
        <td>{row.requestStatus}</td>
        <td>{row.employeeName ?? '—'}</td>
        <td>{row.elapsedHours == null ? 'In progress / unavailable' : `${numberFormat.format(row.elapsedHours)} hrs`}</td>
      </tr>)
    }
    if (tab === 'leave' && leavePage) {
      return leavePage.items.map((row) => <tr key={row.requestId}>
        <td><strong>{row.employeeName}</strong><small>#{row.requestId} · {row.departmentName ?? 'Unassigned'}</small></td>
        <td>{row.leaveType}</td>
        <td>{formatDate(row.startDate)} – {formatDate(row.endDate)}</td>
        <td>{row.calculatedLeaveUnits == null ? `${row.totalLeaveDays} calendar days` : `${formatUnits(row.calculatedLeaveUnits)} units`}</td>
        <td><span className={`report-status report-status-${row.requestStatus.toLowerCase()}`}>{row.requestStatus}</span></td>
        <td>{formatUnits(row.used)}</td>
        <td>{formatUnits(row.available)}</td>
      </tr>)
    }
    return null
  }

  const columns = tab === 'requests'
    ? ['Request', 'Type', 'Status', 'Priority', 'Employee', 'Department', 'Submitted']
    : tab === 'approvals'
      ? ['Request', 'Decision', 'Manager', 'Stage', 'Employee', 'Department', 'Decided']
      : tab === 'workflows'
        ? ['Request', 'Workflow', 'Workflow state', 'Stage summary', 'Request status', 'Employee', 'Elapsed']
        : ['Employee', 'Leave type', 'Leave dates', 'Requested', 'Status', 'Used', 'Available']

  return <section className="reports-page" aria-label="Reports">
    <header className="reports-heading">
      <div><span className="section-eyebrow">INSIGHTS</span><h1>Reports</h1><p>Search, filter, sort, and page through role-authorized business reports.</p></div>
      <div className="reports-heading-actions">
        <div className="reports-export-actions" aria-label="Export report">
          <button
            type="button"
            className="reports-export-button"
            disabled={exportingFormat !== null}
            aria-busy={exportingFormat === 'xlsx'}
            onClick={() => void exportReport('xlsx')}
          >{exportingFormat === 'xlsx' ? 'Preparing Excel…' : 'Export Excel'}</button>
          <button
            type="button"
            className="reports-export-button"
            disabled={exportingFormat !== null}
            aria-busy={exportingFormat === 'pdf'}
            onClick={() => void exportReport('pdf')}
          >{exportingFormat === 'pdf' ? 'Preparing PDF…' : 'Export PDF'}</button>
        </div>
        <span className="reports-total" aria-live="polite">{numberFormat.format(totalCount)} records</span>
      </div>
    </header>
    <div className="reports-tabs" role="tablist" aria-label="Report type">
      {tabs.map((item) => <button
        type="button"
        role="tab"
        aria-selected={tab === item.id}
        className={tab === item.id ? 'selected' : ''}
        key={item.id}
        onClick={() => {
          setTab(item.id)
          setPage(1)
          setSortBy(item.id === 'approvals'
            ? 'decidedAtUtc'
            : item.id === 'workflows'
              ? 'startedAtUtc'
              : item.id === 'leave'
                ? 'startDate'
                : 'createdAtUtc')
        }}
      >{item.label}</button>)}
    </div>
    <div className="reports-filter-card">
      <div className="reports-filter-grid">
        <label>From<input type="date" value={from} max={to} onChange={(event) => changeFilter(() => setFrom(event.target.value))} /></label>
        <label>To<input type="date" value={to} min={from} max={today} onChange={(event) => changeFilter(() => setTo(event.target.value))} /></label>
        <label className="reports-search">Search<input type="search" value={search} maxLength={100} placeholder="Request, employee, or type" onChange={(event) => changeFilter(() => setSearch(event.target.value))} /></label>
        <label>Department<select value={departmentId} onChange={(event) => changeFilter(() => setDepartmentId(event.target.value))}>
          <option value="">All authorized departments</option>
          {options?.departments.map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}
        </select></label>
        <label>Request type<select value={requestTypeId} onChange={(event) => changeFilter(() => setRequestTypeId(event.target.value))}>
          <option value="">All request types</option>
          {options?.requestTypes.map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}
        </select></label>
        <label>Status<select value={status} onChange={(event) => changeFilter(() => setStatus(event.target.value))}>
          <option value="">All statuses</option>
          {['Pending', 'Approved', 'Rejected', 'Cancelled'].map((value) => <option value={value} key={value}>{value}</option>)}
        </select></label>
        <label>Priority<select value={priority} onChange={(event) => changeFilter(() => setPriority(event.target.value))}>
          <option value="">All priorities</option>
          {['Low', 'Medium', 'High', 'Urgent'].map((value) => <option value={value} key={value}>{value}</option>)}
        </select></label>
        {role !== 'Employee' && <label>Employee<select value={employeeUserId} onChange={(event) => changeFilter(() => setEmployeeUserId(event.target.value))}>
          <option value="">All authorized employees</option>
          {options?.employees.map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}
        </select></label>}
        {role !== 'Employee' && <label>Manager<select value={managerUserId} onChange={(event) => changeFilter(() => setManagerUserId(event.target.value))}>
          <option value="">All authorized managers</option>
          {options?.managers.map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}
        </select></label>}
        <label>Rows<select value={pageSize} onChange={(event) => changeFilter(() => setPageSize(Number(event.target.value)))}>
          {[10, 20, 50, 100].map((size) => <option key={size} value={size}>{size} per page</option>)}
        </select></label>
      </div>
      <button type="button" className="reports-reset" onClick={() => {
        setFrom(initialFrom)
        setTo(today)
        setSearch('')
        setDepartmentId('')
        setRequestTypeId('')
        setStatus('')
        setPriority('')
        setEmployeeUserId('')
        setManagerUserId('')
        setPage(1)
      }}>Reset filters</button>
    </div>
    {optionsError && <div className="reports-error" role="alert">Filter options could not be loaded. {optionsError}</div>}
    {error && <div className="reports-error" role="alert">{error}</div>}
    {exportError && <div className="reports-error" role="alert">{exportError}</div>}
    <div className="reports-table-card">
      {loading && <div className="reports-loading" role="status" aria-live="polite">Loading report…</div>}
      {!loading && !error && totalCount === 0 && <div className="reports-empty" role="status">
        <strong>No report records found</strong><span>Adjust the filters or date range and try again.</span>
      </div>}
      {(!loading || totalCount > 0) && totalCount > 0 && <div className="reports-table-scroll">
        <table className="reports-table">
          <thead><tr>{columns.map((column, index) => {
            const fieldsByTab = tab === 'requests'
              ? ['title', 'requestTypeName', 'status', 'priority', 'employeeName', 'departmentName', 'createdAtUtc']
              : tab === 'approvals'
                ? ['requestTitle', 'decision', 'managerName', 'stageSequence', 'employeeName', 'departmentName', 'decidedAtUtc']
                : tab === 'workflows'
                  ? ['requestTitle', 'workflowName', 'workflowStatus', 'requestTitle', 'requestStatus', 'employeeName', 'elapsedHours']
                  : ['employeeName', 'leaveType', 'startDate', 'calculatedLeaveUnits', 'requestStatus', 'createdAtUtc', 'available']
            const field = fieldsByTab[index]
            return field === 'available'
              ? <th key={column}>{column}</th>
              : <th key={column} aria-sort={sortBy === field ? sortDirection === 'asc' ? 'ascending' : 'descending' : 'none'}>
                <button type="button" onClick={() => sort(field)}>{column}{sortBy === field ? sortDirection === 'asc' ? ' ↑' : ' ↓' : ''}</button>
              </th>
          })}</tr></thead>
          <tbody>{renderRows()}</tbody>
        </table>
      </div>}
      <footer className="reports-pagination">
        <span>{totalCount === 0 ? 'No records' : `Page ${page} of ${totalPages} · ${numberFormat.format(totalCount)} records`}</span>
        <div><button type="button" disabled={page <= 1 || loading} onClick={() => setPage((current) => Math.max(1, current - 1))}>Previous</button>
          <button type="button" disabled={page >= totalPages || loading} onClick={() => setPage((current) => Math.min(totalPages, current + 1))}>Next</button></div>
      </footer>
    </div>
  </section>
}
