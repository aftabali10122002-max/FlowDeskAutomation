import { createContext } from 'react'
import type { DashboardAnalytics } from './api'

export type RangePreset = '7' | '30' | '90' | 'custom'

export type AnalyticsContextValue = {
  preset: RangePreset
  today: string
  range: { from: string; to: string }
  data: DashboardAnalytics | null
  loading: boolean
  error: string | null
  changePreset: (preset: RangePreset) => void
  changeFrom: (from: string) => void
  changeTo: (to: string) => void
  resetFilters: () => void
  retry: () => void
}

export const AnalyticsContext = createContext<AnalyticsContextValue | null>(null)
