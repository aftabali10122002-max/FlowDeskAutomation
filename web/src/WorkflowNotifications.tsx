import { useCallback, useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import {
  getNotifications,
  markNotificationRead,
  type WorkflowNotification,
} from './api'
import { formatDate } from './dateFormatting'

export function WorkflowNotifications({
  onError,
  onNavigateToRequest,
  onRequestNotification,
  onIncomingNotification,
  unreadCount,
  onUnreadCountChange,
  getErrorMessage,
}: {
  onError: (message: string) => void
  onNavigateToRequest: (requestId: number) => Promise<void>
  onRequestNotification: (requestId: number, notificationType: string) => Promise<void>
  onIncomingNotification: (notification: WorkflowNotification) => void
  unreadCount: number
  onUnreadCountChange: (count: number) => void
  getErrorMessage: (error: unknown) => string
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
      setListError(getErrorMessage(caught))
    } finally {
      setLoading(false)
    }
  }, [getErrorMessage, onUnreadCountChange])

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
        const message = getErrorMessage(caught)
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
