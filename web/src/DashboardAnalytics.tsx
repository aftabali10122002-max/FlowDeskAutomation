import { memo, useContext } from 'react'
import type { ReactNode } from 'react'
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { DashboardAnalytics } from './api'
import { AnalyticsContext } from './DashboardAnalyticsContext'
import type { RangePreset } from './DashboardAnalyticsContext'
import { priorityColor, requestTypeColor } from './DashboardAnalyticsColors'
import './DashboardAnalytics.css'

type AnalyticsMode = 'overview' | 'full'

const chartColors = ['#0D9488', '#10B981', '#F59E0B', '#EF4444', '#64748B']
const statusColors: Record<string, string> = {
  pending: '#F59E0B',
  approved: '#10B981',
  rejected: '#EF4444',
  cancelled: '#64748B',
}
const numberFormat = new Intl.NumberFormat()
const hourFormat = new Intl.NumberFormat(undefined, {
  maximumFractionDigits: 1,
})

function formatAxisDate(value: string) {
  const date = value.length === 7
    ? new Date(`${value}-01T00:00:00Z`)
    : new Date(`${value}T00:00:00Z`)
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    ...(value.length === 10 ? { day: 'numeric' as const } : {}),
    timeZone: 'UTC',
  }).format(date)
}

function formatHours(value: number | null) {
  return value === null ? '—' : `${hourFormat.format(value)} hrs`
}

function shortRequestTypeLabel(name: string) {
  const labels: Record<string, string> = {
    'expense reimbursement': 'Expense reimburse.',
    'material purchase': 'Material purchase',
    'work from home': 'Work from home',
    'other / legacy': 'Other / legacy',
  }
  return labels[name.toLowerCase()] ?? name
}

function ChartPanel({
  title,
  description,
  label,
  children,
  empty = false,
  compact = false,
  wide = false,
}: {
  title: string
  description: string
  label: string
  children: ReactNode
  empty?: boolean
  compact?: boolean
  wide?: boolean
}) {
  return (
    <section className={`analytics-panel${compact ? ' analytics-panel-compact' : ''}${wide ? ' analytics-trend-panel' : ''}`} aria-label={label}>
      <header className="analytics-panel-heading">
        <div>
          <h2>{title}</h2>
          <p>{description}</p>
        </div>
      </header>
      {empty
        ? <div className="analytics-empty" role="status">No data for this date range.</div>
        : <div className="analytics-chart" role="img" aria-label={label}>{children}</div>}
    </section>
  )
}

function DateRangeControls({
  preset,
  from,
  to,
  today,
  onPresetChange,
  onFromChange,
  onToChange,
}: {
  preset: RangePreset
  from: string
  to: string
  today: string
  onPresetChange: (preset: RangePreset) => void
  onFromChange: (value: string) => void
  onToChange: (value: string) => void
}) {
  return (
    <div className="analytics-date-controls" aria-label="Analytics date range">
      <div className="analytics-range-buttons" role="group" aria-label="Quick date range">
        {(['7', '30', '90', 'custom'] as const).map((option) => (
          <button
            type="button"
            key={option}
            className={preset === option ? 'selected' : ''}
            aria-pressed={preset === option}
            onClick={() => onPresetChange(option)}
          >
            {option === 'custom' ? 'Custom' : `Last ${option} days`}
          </button>
        ))}
      </div>
      {preset === 'custom' && (
        <div className="analytics-custom-range">
          <label>From<input aria-label="Analytics start date" type="date" value={from} max={to} required onChange={(event) => onFromChange(event.target.value)} /></label>
          <span aria-hidden="true">to</span>
          <label>Through<input aria-label="Analytics end date" type="date" value={to} min={from} max={today} required onChange={(event) => onToChange(event.target.value)} /></label>
        </div>
      )}
    </div>
  )
}

function LoadingPanels() {
  return (
    <>
      <div className="analytics-metrics-grid analytics-metrics-skeleton" aria-label="Loading analytics metrics" aria-busy="true">
        {[0, 1, 2, 3, 4].map((item) => <div className="analytics-metric-card" key={item}><span /><strong /></div>)}
      </div>
      <div className="analytics-grid" aria-label="Loading analytics charts" aria-busy="true">
        {[0, 1].map((item) => <div className="analytics-panel analytics-skeleton" key={item}><span /><span /><div /></div>)}
      </div>
    </>
  )
}

function MetricCard({ label, value, detail }: {
  label: string
  value: string
  detail?: string
}) {
  return <article className="analytics-metric-card">
    <span>{label}</span>
    <strong>{value}</strong>
    {detail && <small>{detail}</small>}
  </article>
}

function AnalyticsSummary({ data }: { data: DashboardAnalytics }) {
  const admin = data.adminSummary
  if (data.role === 'Admin' && admin) {
    const approvalRate = admin.approvalRatePercent === null
      ? '—'
      : `${hourFormat.format(admin.approvalRatePercent)}%`
    return <div className="analytics-metrics-grid" aria-label="Admin analytics summary">
      <MetricCard label="Total requests" value={numberFormat.format(admin.totalRequests)} />
      <MetricCard label="Pending requests" value={numberFormat.format(admin.pendingRequests)} />
      <MetricCard label="Approved requests" value={numberFormat.format(admin.approvedRequests)} />
      <MetricCard label="Rejected requests" value={numberFormat.format(admin.rejectedRequests)} />
      <MetricCard label="Cancelled requests" value={numberFormat.format(admin.cancelledRequests)} />
      <MetricCard label="Approval rate" value={approvalRate} detail="Approved / approved + rejected" />
      <MetricCard label="Average approval time" value={formatHours(admin.averageApprovalTimeHours)} detail="Stage activation to decision" />
      <MetricCard label="Overdue approvals" value={numberFormat.format(admin.overdueRequests)} detail="Active workflow stages past due" />
      <MetricCard label="Active employees" value={numberFormat.format(admin.activeEmployees)} />
      <MetricCard label="Active managers" value={numberFormat.format(admin.activeManagers)} />
    </div>
  }

  const manager = data.managerSummary
  if (data.role === 'Manager' && manager) {
    return <div className="analytics-metrics-grid analytics-metrics-manager" aria-label="Manager analytics summary">
      <MetricCard label="Assigned requests" value={numberFormat.format(manager.assignedRequests)} />
      <MetricCard label="Pending approvals" value={numberFormat.format(manager.pendingApprovals)} />
      <MetricCard label="Approved requests" value={numberFormat.format(manager.approvedRequests)} />
      <MetricCard label="Rejected requests" value={numberFormat.format(manager.rejectedRequests)} />
      <MetricCard label="Overdue approvals" value={numberFormat.format(manager.overdueApprovals)} detail="Active workflow stages past due" />
      <MetricCard label="Average decision time" value={formatHours(manager.averageDecisionTimeHours)} />
    </div>
  }

  const employee = data.employeeSummary
  return employee && <div className="analytics-metrics-grid analytics-metrics-employee" aria-label="Employee analytics summary">
    <MetricCard label="My total requests" value={numberFormat.format(employee.totalRequests)} />
    <MetricCard label="Pending requests" value={numberFormat.format(employee.pendingRequests)} />
    <MetricCard label="Approved requests" value={numberFormat.format(employee.approvedRequests)} />
    <MetricCard label="Rejected requests" value={numberFormat.format(employee.rejectedRequests)} />
    <MetricCard label="Cancelled requests" value={numberFormat.format(employee.cancelledRequests)} />
  </div>
}

function RequestTypeChart({ data }: { data: DashboardAnalytics['requestTypes'] }) {
  return (
    <BarChart data={data} layout="vertical" barCategoryGap="25%" barGap={8} margin={{ left: 16, right: 16, top: 6, bottom: 18 }}>
      <CartesianGrid stroke="#E7ECEF" horizontal={false} />
      <XAxis type="number" allowDecimals={false} label={{ value: 'Requests', position: 'insideBottom', offset: -8, fill: '#71858A', fontSize: 10 }} />
      <YAxis type="category" dataKey="name" width={142} tickFormatter={shortRequestTypeLabel} tick={{ fontSize: 10, fill: '#71858A' }} />
      <Tooltip
        cursor={{ fill: '#F0FDFA' }}
        formatter={(value) => [numberFormat.format(Number(value)), 'Requests']}
        content={({ active, payload }) => {
          const item = payload?.[0]
          if (!active || !item) return null
          const requestType = item.payload as { code: string; name: string; count: number }
          return (
            <div className="analytics-tooltip">
              <span className="analytics-tooltip-swatch" style={{ backgroundColor: requestTypeColor(requestType.code, requestType.name) }} />
              <span>{requestType.name}</span>
              <strong>{numberFormat.format(requestType.count)} requests</strong>
            </div>
          )
        }}
      />
      <Bar dataKey="count" name="Requests" barSize={18} maxBarSize={24} radius={[0, 6, 6, 0]}>
        {data.map((item) => <Cell key={item.code || item.name} fill={requestTypeColor(item.code, item.name)} />)}
      </Bar>
    </BarChart>
  )
}

function RequestTypePanel({
  data,
  label,
}: {
  data: DashboardAnalytics['requestTypes']
  label: string
}) {
  return (
    <ChartPanel title="Request types" description="Legacy requests are grouped as Other / Legacy" label={label} empty={!data.some((item) => item.count > 0)}>
      <ResponsiveContainer width="100%" height="100%">
        <RequestTypeChart data={data} />
      </ResponsiveContainer>
    </ChartPanel>
  )
}

const AnalyticsCharts = memo(function AnalyticsCharts({ data }: { data: DashboardAnalytics }) {
  const role = data.role
  const hasStatus = data.statusDistribution.some((item) => item.count > 0)
  const hasTrend = data.submissionTrend.some((item) => item.count > 0)

  if (role === 'Admin') {
    return (
      <div className="analytics-grid">
        <ChartPanel title="Request status" description={`${numberFormat.format(data.totalRequests)} requests in range`} label="Request status distribution donut chart" empty={!hasStatus}>
          <div className="analytics-donut-wrap">
            <div className="analytics-donut-total" aria-hidden="true"><strong>{numberFormat.format(data.totalRequests)}</strong><span>requests</span></div>
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={data.statusDistribution} dataKey="count" nameKey="label" innerRadius="62%" outerRadius="82%" paddingAngle={3} stroke="none" isAnimationActive>
                  {data.statusDistribution.map((item, index) => <Cell key={item.label} fill={statusColors[item.label.toLowerCase()] ?? chartColors[index % chartColors.length]} />)}
                </Pie>
                <Tooltip formatter={(value, name) => [`${numberFormat.format(Number(value))} (${data.totalRequests ? (Number(value) / data.totalRequests * 100).toFixed(1) : '0.0'}%)`, String(name)]} />
                <Legend verticalAlign="bottom" height={34} />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </ChartPanel>
        <TrendPanel title="Monthly request trends" description="Requests submitted over time" label="Monthly request submission trend" data={data.submissionTrend} empty={!hasTrend} />
        <ChartPanel title="Department performance" description="Approved, pending, and rejected requests" label="Department request status comparison" empty={!data.departmentPerformance.some((item) => item.approved + item.pending + item.rejected > 0)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data.departmentPerformance} layout="vertical" barCategoryGap="25%" barGap={8} margin={{ left: 16, right: 14, top: 8, bottom: 18 }}>
              <CartesianGrid stroke="#E7ECEF" horizontal={false} />
              <XAxis type="number" allowDecimals={false} tickFormatter={(value) => numberFormat.format(Number(value))} label={{ value: 'Requests', position: 'insideBottom', offset: -8, fill: '#71858A', fontSize: 10 }} />
              <YAxis type="category" dataKey="department" width={105} tick={{ fontSize: 11, fill: '#71858A' }} />
              <Tooltip formatter={(value, name) => [numberFormat.format(Number(value)), String(name)]} />
              <Legend />
              <Bar dataKey="approved" name="Approved" stackId="status" fill="#10B981" maxBarSize={24} radius={[0, 5, 5, 0]} />
              <Bar dataKey="pending" name="Pending" stackId="status" fill="#F59E0B" maxBarSize={24} />
              <Bar dataKey="rejected" name="Rejected" stackId="status" fill="#EF4444" maxBarSize={24} />
            </BarChart>
          </ResponsiveContainer>
        </ChartPanel>
        <ChartPanel title="Approval performance" description="Average elapsed time from stage activation to decision" label="Average approval time by department" empty={data.approvalPerformance.length === 0}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data.approvalPerformance} layout="vertical" barCategoryGap="25%" barGap={8} margin={{ left: 16, right: 18, top: 8, bottom: 18 }}>
              <CartesianGrid stroke="#E7ECEF" horizontal={false} />
              <XAxis type="number" tickFormatter={(value) => `${hourFormat.format(Number(value))}h`} label={{ value: 'Average hours', position: 'insideBottom', offset: -8, fill: '#71858A', fontSize: 10 }} />
              <YAxis type="category" dataKey="department" width={105} tick={{ fontSize: 11, fill: '#71858A' }} />
              <Tooltip formatter={(value, _name, item) => [`${hourFormat.format(Number(value))} hrs · ${numberFormat.format(Number(item.payload.decisions))} decisions`, 'Average decision time']} />
              <Bar dataKey="averageHours" name="Average hours" fill="#0F766E" barSize={18} maxBarSize={24} radius={[0, 6, 6, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartPanel>
        <RequestTypePanel data={data.requestTypes} label="Request type volume" />
        <ChartPanel title="Priority distribution" description="Requests by assigned priority" label="Request priority distribution" empty={!data.priorityDistribution.some((item) => item.count > 0)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data.priorityDistribution} barCategoryGap="25%" barGap={8} margin={{ left: 8, right: 10, top: 8, bottom: 8 }}>
              <CartesianGrid stroke="#E7ECEF" vertical={false} />
              <XAxis dataKey="label" tick={{ fontSize: 10 }} />
              <YAxis allowDecimals={false} tick={{ fill: '#71858A', fontSize: 10 }} label={{ value: 'Requests', angle: -90, position: 'insideLeft', fill: '#71858A', fontSize: 10 }} />
              <Tooltip formatter={(value) => [numberFormat.format(Number(value)), 'Requests']} />
              <Bar dataKey="count" name="Requests" barSize={24} maxBarSize={36} radius={[6, 6, 0, 0]}>
                {data.priorityDistribution.map((item) => <Cell key={item.label} fill={priorityColor(item.label)} />)}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </ChartPanel>
        <ChartPanel title="Workflow bottlenecks" description="Active approval stages grouped by sequence; overdue stages have passed their due time" label="Workflow bottlenecks by approval stage" empty={!data.workflowBottlenecks.some((item) => item.pendingApprovals > 0)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data.workflowBottlenecks} layout="vertical" barCategoryGap="25%" margin={{ left: 16, right: 16, top: 8, bottom: 18 }}>
              <CartesianGrid stroke="#E7ECEF" horizontal={false} />
              <XAxis type="number" allowDecimals={false} label={{ value: 'Approvals', position: 'insideBottom', offset: -8, fill: '#71858A', fontSize: 10 }} />
              <YAxis type="category" dataKey="stage" width={72} tick={{ fontSize: 10, fill: '#71858A' }} />
              <Tooltip formatter={(value, name) => [numberFormat.format(Number(value)), String(name)]} />
              <Legend />
              <Bar dataKey="pendingApprovals" name="Pending" fill="#0D9488" maxBarSize={24} />
              <Bar dataKey="overdueApprovals" name="Overdue" fill="#EF4444" maxBarSize={24} />
            </BarChart>
          </ResponsiveContainer>
        </ChartPanel>
      </div>
    )
  }

  if (role === 'Manager') {
    return (
      <div className="analytics-grid">
        <TrendPanel title="Assigned request trend" description="Unique requests assigned to you" label="Assigned request trend" data={data.assignedRequestTrend} empty={!data.assignedRequestTrend.some((item) => item.count > 0)} />
        <ChartPanel title="Pending vs completed" description="Unique requests assigned to you" label="Pending versus completed requests" empty={!data.managerRequestStatus.some((item) => item.count > 0)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data.managerRequestStatus} barCategoryGap="25%" barGap={8} margin={{ left: 10, right: 10, top: 18, bottom: 8 }}>
              <CartesianGrid stroke="#E7ECEF" vertical={false} />
              <XAxis dataKey="label" />
              <YAxis allowDecimals={false} tick={{ fill: '#71858A', fontSize: 10 }} label={{ value: 'Requests', angle: -90, position: 'insideLeft', fill: '#71858A', fontSize: 10 }} />
              <Tooltip formatter={(value) => [numberFormat.format(Number(value)), 'Requests']} />
              <Bar dataKey="count" name="Requests" barSize={24} maxBarSize={36} fill="#0D9488" radius={[6, 6, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartPanel>
        <TrendPanel title="Monthly approval activity" description="Decisions made by you" label="Manager decision activity" data={data.decisionActivity} empty={!data.decisionActivity.some((item) => item.count > 0)} />
        <section className="analytics-panel analytics-average" aria-label="Average decision time">
          <div className="analytics-panel-heading"><div><h2>Average decision time</h2><p>Only your decisions with valid workflow timestamps</p></div></div>
          <strong>{formatHours(data.averageDecisionTimeHours)}</strong>
          {data.averageDecisionTimeHours === null && <span>No completed decisions with timing data.</span>}
        </section>
        <ChartPanel title="Priority distribution" description="Unique assigned requests; missing priorities are shown as Unspecified" label="Assigned request priority distribution" empty={!data.priorityDistribution.some((item) => item.count > 0)}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data.priorityDistribution} barCategoryGap="25%" barGap={8} margin={{ left: 8, right: 10, top: 8, bottom: 8 }}>
              <CartesianGrid stroke="#E7ECEF" vertical={false} />
              <XAxis dataKey="label" tick={{ fontSize: 10 }} />
              <YAxis allowDecimals={false} tick={{ fill: '#71858A', fontSize: 10 }} label={{ value: 'Requests', angle: -90, position: 'insideLeft', fill: '#71858A', fontSize: 10 }} />
              <Tooltip formatter={(value) => [numberFormat.format(Number(value)), 'Requests']} />
              <Bar dataKey="count" name="Requests" barSize={24} maxBarSize={36} fill="#10B981" radius={[6, 6, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ChartPanel>
        <RequestTypePanel data={data.requestTypes} label="Assigned request type breakdown" />
      </div>
    )
  }

  return (
    <div className="analytics-grid">
      <ChartPanel title="My request status" description={`${numberFormat.format(data.totalRequests)} of your requests in range`} label="My request status distribution" empty={!hasStatus}>
        <div className="analytics-donut-wrap">
          <div className="analytics-donut-total" aria-hidden="true"><strong>{numberFormat.format(data.totalRequests)}</strong><span>my requests</span></div>
          <ResponsiveContainer width="100%" height="100%">
            <PieChart>
              <Pie data={data.statusDistribution} dataKey="count" nameKey="label" innerRadius="62%" outerRadius="82%" paddingAngle={3} stroke="none">
                {data.statusDistribution.map((item, index) => <Cell key={item.label} fill={statusColors[item.label.toLowerCase()] ?? chartColors[index % chartColors.length]} />)}
              </Pie>
              <Tooltip formatter={(value, name) => [`${numberFormat.format(Number(value))} (${data.totalRequests ? (Number(value) / data.totalRequests * 100).toFixed(1) : '0.0'}%)`, String(name)]} />
              <Legend verticalAlign="bottom" height={34} />
            </PieChart>
          </ResponsiveContainer>
        </div>
      </ChartPanel>
      <TrendPanel title="Monthly submission activity" description="Your requests submitted over time" label="Employee monthly submission activity" data={data.submissionTrend} empty={!hasTrend} />
      <RequestTypePanel data={data.requestTypes} label="My request type breakdown" />
      <ChartPanel title="Approval progress" description="Request-level status; each request is counted once" label="My approval progress summary" empty={!data.approvalProgress.some((item) => item.count > 0)}>
        <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data.approvalProgress} barCategoryGap="25%" barGap={8} margin={{ left: 10, right: 12, top: 8, bottom: 8 }}>
            <CartesianGrid stroke="#E7ECEF" vertical={false} />
            <XAxis dataKey="label" tick={{ fontSize: 10 }} />
            <YAxis allowDecimals={false} tick={{ fill: '#71858A', fontSize: 10 }} label={{ value: 'Requests', angle: -90, position: 'insideLeft', fill: '#71858A', fontSize: 10 }} />
            <Tooltip formatter={(value) => [numberFormat.format(Number(value)), 'Requests']} />
            <Bar dataKey="count" name="Requests" barSize={24} maxBarSize={36} fill="#10B981" radius={[6, 6, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </ChartPanel>
    </div>
  )
})

function TrendPanel({
  title,
  description,
  label,
  data,
  empty,
  compact = false,
  wide = true,
}: {
  title: string
  description: string
  label: string
  data: { date: string; count: number }[]
  empty: boolean
  compact?: boolean
  wide?: boolean
}) {
  return (
    <ChartPanel title={title} description={description} label={label} empty={empty} compact={compact} wide={wide}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ left: 0, right: 8, top: 12, bottom: 0 }}>
          <defs><linearGradient id={`analytics-fill-${label.replaceAll(' ', '-')}`} x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#0D9488" stopOpacity={0.25} /><stop offset="95%" stopColor="#10B981" stopOpacity={0.02} /></linearGradient></defs>
          <CartesianGrid stroke="#E7ECEF" vertical={false} />
          <XAxis dataKey="date" tickFormatter={formatAxisDate} minTickGap={24} tick={{ fontSize: 10, fill: '#71858A' }} label={{ value: 'Date', position: 'insideBottom', offset: -2, fill: '#71858A', fontSize: 10 }} />
          <YAxis allowDecimals={false} width={34} tick={{ fontSize: 10, fill: '#71858A' }} label={{ value: 'Requests', angle: -90, position: 'insideLeft', fill: '#71858A', fontSize: 10 }} />
          <Tooltip labelFormatter={(value) => formatAxisDate(String(value))} formatter={(value) => [numberFormat.format(Number(value)), 'Requests']} />
          <Area type="monotone" dataKey="count" name="Requests" stroke="#0D9488" strokeWidth={2.5} fill={`url(#analytics-fill-${label.replaceAll(' ', '-')})`} activeDot={{ r: 5, fill: '#10B981', stroke: '#fff', strokeWidth: 2 }} isAnimationActive />
        </AreaChart>
      </ResponsiveContainer>
    </ChartPanel>
  )
}

export function DashboardAnalytics({
  mode,
}: {
  mode: AnalyticsMode
}) {
  const context = useContext(AnalyticsContext)
  if (!context) throw new Error('DashboardAnalytics must be rendered inside DashboardAnalyticsProvider.')
  const { preset, today, range, data, loading, error } = context

  if (mode === 'overview') {
    const role = data?.role ?? 'Employee'
    const trend = role === 'Manager' ? data?.assignedRequestTrend : data?.submissionTrend
    const hasTrend = Boolean(trend?.some((item) => item.count > 0))
    return (
      <section className="dashboard-analytics dashboard-analytics-compact" aria-label="Dashboard analytics">
        <div className="analytics-toolbar">
          <div><div className="section-eyebrow">AT A GLANCE</div><h2>{role === 'Manager' ? 'Assigned request activity' : 'Request activity'}</h2><p>{formatAxisDate(range.from)} – {formatAxisDate(range.to)}</p></div>
        </div>
        {error && <div className="analytics-error" role="alert">{error}<button className="text-button" onClick={context.retry}>Retry</button></div>}
        {loading && !data
          ? <div className="analytics-compact-skeleton" role="status" aria-label="Loading request activity" />
          : data && trend && <TrendPanel
            title={role === 'Manager' ? 'Assigned request trend' : 'Request submissions'}
            description={`${numberFormat.format(data.totalRequests)} requests during this period`}
            label="Dashboard request activity trend"
            data={trend}
            empty={!hasTrend}
            compact
          />}
        {loading && data && <div className="analytics-refreshing" role="status">Updating analytics…</div>}
      </section>
    )
  }

  return (
    <section className="dashboard-analytics analytics-page" aria-label="Analytics">
      <div className="analytics-toolbar">
        <div><div className="section-eyebrow">INSIGHTS</div><h1>Analytics</h1><p>Explore activity across the selected date range.</p></div>
        <div className="analytics-filter-area">
          <DateRangeControls
            preset={preset}
            from={range.from}
            to={range.to}
            today={today}
            onPresetChange={context.changePreset}
            onFromChange={context.changeFrom}
            onToChange={context.changeTo}
          />
          <button className="analytics-reset" type="button" onClick={context.resetFilters}>Reset filters</button>
        </div>
      </div>
      {error && <div className="analytics-error" role="alert">{error}<button className="text-button" onClick={context.retry}>Retry</button></div>}
      {loading && !data
        ? <LoadingPanels />
        : data && <>
          {mode === 'full' && <AnalyticsSummary data={data} />}
          <AnalyticsCharts data={data} />
        </>}
      {loading && data && <div className="analytics-refreshing" role="status">Updating analytics…</div>}
    </section>
  )
}
