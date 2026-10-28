import { useEffect, useState } from 'react'
import {
  ApiError,
  getAdminRequestTypes,
  setRequestTypeEnabled,
  type RequestType,
} from './api'

function errorMessage(error: unknown) {
  return error instanceof ApiError
    ? error.message
    : 'Request type settings could not be updated.'
}

export function RequestTypeManagement() {
  const [requestTypes, setRequestTypes] = useState<RequestType[]>([])
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<number | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    void getAdminRequestTypes().then((result) => {
      if (active) setRequestTypes(result)
    }).catch((caught: unknown) => {
      if (active) setError(errorMessage(caught))
    }).finally(() => {
      if (active) setLoading(false)
    })
    return () => { active = false }
  }, [])

  async function toggle(requestType: RequestType) {
    const nextEnabled = !requestType.isEnabled
    if (!nextEnabled && !window.confirm(
      `Disable ${requestType.displayName}? Existing requests and in-progress workflows will be preserved; employees will no longer be able to submit new requests of this type.`,
    )) return

    setBusyId(requestType.id)
    setError('')
    try {
      const updated = await setRequestTypeEnabled(
        requestType.id,
        nextEnabled,
      )
      setRequestTypes((current) => current.map((item) =>
        item.id === updated.id ? updated : item))
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setBusyId(null)
    }
  }

  return <>
    <div className="welcome-row">
      <div><div className="eyebrow">WORKSPACE · ADMIN</div><h1>Request types</h1><p className="page-subtitle">Enable or disable employee request forms without changing existing requests or workflow history.</p></div>
    </div>
    <section className="content-card request-type-management">
      <div className="card-heading"><div><div className="section-eyebrow">CONFIGURATION</div><h2>Available request types</h2></div></div>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      {loading
        ? <div className="loading-strip">Loading request types…</div>
        : <ul className="request-type-list">{requestTypes.map((requestType) =>
          <li key={requestType.id}>
            <div><strong>{requestType.displayName}</strong><small>{requestType.code.replaceAll('_', ' ')} · {requestType.requiresAmount ? 'Amount required' : 'No amount required'}</small></div>
            <span className={`user-active-status ${requestType.isEnabled ? 'active' : 'inactive'}`}>{requestType.isEnabled ? 'Enabled' : 'Disabled'}</span>
            <button
              className={`button ${requestType.isEnabled ? 'button-secondary' : 'button-primary'}`}
              disabled={busyId !== null}
              onClick={() => { void toggle(requestType) }}
            >{busyId === requestType.id
              ? 'Saving…'
              : requestType.isEnabled ? 'Disable' : 'Enable'}</button>
          </li>)}</ul>}
      <p className="profile-readonly">Request-type codes and field definitions are fixed by the application. Routing is configured separately in Approval Workflows.</p>
    </section>
  </>
}
