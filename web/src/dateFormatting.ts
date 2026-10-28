export function formatDate(date: string) {
  const parsed = new Date(date)
  return Number.isNaN(parsed.getTime())
    ? '—'
    : new Intl.DateTimeFormat(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      ...(/^\d{4}-\d{2}-\d{2}$/.test(date) ? { timeZone: 'UTC' } : {}),
    }).format(parsed)
}
