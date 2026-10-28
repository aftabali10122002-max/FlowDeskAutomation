import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { ApiError, getDashboardAnalytics } from './api'
import type { DashboardAnalytics } from './api'
import { AnalyticsContext } from './DashboardAnalyticsContext'
import type { AnalyticsContextValue, RangePreset } from './DashboardAnalyticsContext'

function utcDateString(date: Date) {
  return date.toISOString().slice(0, 10)
}

function dateRange(days: number) {
  const end = new Date()
  const start = new Date(end)
  start.setUTCDate(start.getUTCDate() - (days - 1))
  return { from: utcDateString(start), to: utcDateString(end) }
}

export function DashboardAnalyticsProvider({ children }: { children: ReactNode }) {
  const [preset, setPreset] = useState<RangePreset>('30')
  const [today] = useState(() => utcDateString(new Date()))
  const [range, setRange] = useState(() => dateRange(30))
  const [data, setData] = useState<DashboardAnalytics | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retryCount, setRetryCount] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    void getDashboardAnalytics(range.from, range.to, controller.signal)
      .then((result) => setData(result))
      .catch((cause: unknown) => {
        if (controller.signal.aborted) return
        setError(cause instanceof ApiError
          ? cause.message
          : 'Analytics could not be loaded. Please try again.')
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [range, retryCount])

  function changePreset(next: RangePreset) {
    setPreset(next)
    if (next !== 'custom') {
      const nextRange = dateRange(Number(next))
      if (range.from === nextRange.from && range.to === nextRange.to) return
      setLoading(true)
      setError(null)
      setRange(nextRange)
    }
  }

  function changeFrom(from: string) {
    if (!from) return
    setLoading(true)
    setError(null)
    setRange((current) => ({ ...current, from }))
  }

  function changeTo(to: string) {
    if (!to) return
    setLoading(true)
    setError(null)
    setRange((current) => ({ ...current, to }))
  }

  function resetFilters() {
    const defaultRange = dateRange(30)
    setPreset('30')
    if (range.from === defaultRange.from && range.to === defaultRange.to) return
    setLoading(true)
    setError(null)
    setRange(defaultRange)
  }

  const value: AnalyticsContextValue = {
    preset,
    today,
    range,
    data,
    loading,
    error,
    changePreset,
    changeFrom,
    changeTo,
    resetFilters,
    retry: () => {
      setLoading(true)
      setError(null)
      setRetryCount((count) => count + 1)
    },
  }

  return <AnalyticsContext.Provider value={value}>{children}</AnalyticsContext.Provider>
}
