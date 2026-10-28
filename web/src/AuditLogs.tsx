import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, getAuditLogs } from './api'
import type { AuditLogItem, AuditLogPage } from './api'
import './AuditLogs.css'

const pageSize = 25

type Filters = {
  action: string
  entityType: string
  from: string
  to: string
}

const emptyFilters: Filters = {
  action: '',
  entityType: '',
  from: '',
  to: '',
}

function messageFrom(error: unknown) {
  return error instanceof ApiError
    ? error.message
    : 'Audit history could not be loaded. Please try again.'
}

function formatTimestamp(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '—'
    : new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(date)
}

function toUtcBoundary(value: string, endOfDay = false) {
  if (!value) return undefined
  return new Date(`${value}T${endOfDay ? '23:59:59.999' : '00:00:00.000'}Z`)
    .toISOString()
}

export function AuditLogs() {
  const [result, setResult] = useState<AuditLogPage | null>(null)
  const [draftFilters, setDraftFilters] = useState(emptyFilters)
  const [filters, setFilters] = useState(emptyFilters)
  const [page, setPage] = useState(1)
  const [reloadKey, setReloadKey] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    void getAuditLogs({
      page,
      pageSize,
      action: filters.action.trim() || undefined,
      entityType: filters.entityType.trim() || undefined,
      fromUtc: toUtcBoundary(filters.from),
      toUtc: toUtcBoundary(filters.to, true),
    }).then((response) => {
      if (active) setResult(response)
    }).catch((caught: unknown) => {
      if (active) setError(messageFrom(caught))
    }).finally(() => {
      if (active) setLoading(false)
    })
    return () => {
      active = false
    }
  }, [filters, page, reloadKey])

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoading(true)
    setError('')
    setPage(1)
    setFilters({ ...draftFilters })
  }

  function changePage(nextPage: number) {
    setLoading(true)
    setError('')
    setPage(nextPage)
  }

  function updateFilter(key: keyof Filters, value: string) {
    setDraftFilters((current) => ({ ...current, [key]: value }))
  }

  const totalPages = result
    ? Math.max(1, Math.ceil(result.totalCount / pageSize))
    : 1

  return <section className="audit-page">
    <header className="welcome-row">
      <div>
        <div className="eyebrow">ADMIN · GOVERNANCE</div>
        <h1>Audit Logs</h1>
        <p className="page-subtitle">
          Review important request, workflow, and user-management actions.
        </p>
      </div>
    </header>

    <form className="audit-filters content-card" onSubmit={applyFilters}>
      <label>
        Action
        <input
          value={draftFilters.action}
          onChange={(event) => updateFilter('action', event.currentTarget.value)}
          placeholder="e.g. request.approved"
          maxLength={60}
        />
      </label>
      <label>
        Entity
        <input
          value={draftFilters.entityType}
          onChange={(event) => updateFilter('entityType', event.currentTarget.value)}
          placeholder="e.g. ApprovalRequest"
          maxLength={60}
        />
      </label>
      <label>
        From
        <input
          type="date"
          value={draftFilters.from}
          onChange={(event) => updateFilter('from', event.currentTarget.value)}
        />
      </label>
      <label>
        To
        <input
          type="date"
          value={draftFilters.to}
          onChange={(event) => updateFilter('to', event.currentTarget.value)}
        />
      </label>
      <button className="button button-primary" type="submit">Apply filters</button>
    </form>

    <div className="audit-results content-card">
      {loading && <p className="audit-state" role="status">Loading audit history…</p>}
      {!loading && error && <div className="audit-state audit-error" role="alert">
        <span>{error}</span>
        <button className="button button-secondary" type="button" onClick={() => {
          setLoading(true)
          setError('')
          setReloadKey((current) => current + 1)
        }}>Retry</button>
      </div>}
      {!loading && !error && result && result.items.length === 0 &&
        <p className="audit-state">No audit records match these filters.</p>}
      {!loading && !error && result && result.items.length > 0 && <>
        <div className="audit-table-scroll">
          <table className="audit-table">
            <thead>
              <tr>
                <th scope="col">Actor</th>
                <th scope="col">Action</th>
                <th scope="col">Target</th>
                <th scope="col">Date and time</th>
                <th scope="col">Details</th>
              </tr>
            </thead>
            <tbody>
              {result.items.map((item: AuditLogItem) => <tr key={item.id}>
                <td>{item.actorName || `User ${item.actorUserId}`}
                  <small>ID {item.actorUserId}</small>
                </td>
                <td><code>{item.action}</code></td>
                <td>{item.entityType} #{item.entityId}</td>
                <td>{formatTimestamp(item.occurredAtUtc)}</td>
                <td>{item.details || '—'}</td>
              </tr>)}
            </tbody>
          </table>
        </div>
        <footer className="audit-pagination">
          <span>
            {result.totalCount} record{result.totalCount === 1 ? '' : 's'} ·
            {' '}Page {result.page} of {totalPages}
          </span>
          <div>
            <button
              className="button button-secondary"
              type="button"
              disabled={page <= 1 || loading}
              onClick={() => changePage(page - 1)}
            >Previous</button>
            <button
              className="button button-secondary"
              type="button"
              disabled={page >= totalPages || loading}
              onClick={() => changePage(page + 1)}
            >Next</button>
          </div>
        </footer>
      </>}
    </div>
  </section>
}
