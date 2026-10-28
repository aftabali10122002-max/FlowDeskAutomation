const requestTypeColors: Record<string, string> = {
  'expense reimbursement': '#0D9488',
  expense_reimbursement: '#0D9488',
  'it access': '#3B82F6',
  it_access: '#3B82F6',
  leave: '#8B5CF6',
  leave_request: '#8B5CF6',
  'leave request': '#8B5CF6',
  material_purchase: '#F59E0B',
  'material purchase': '#F59E0B',
  travel: '#EC4899',
  travel_request: '#EC4899',
  'travel request': '#EC4899',
  work_from_home: '#10B981',
  'work from home': '#10B981',
  other_legacy: '#64748B',
  'other / legacy': '#64748B',
  other: '#64748B',
}

export function requestTypeColor(code: string, name: string) {
  return requestTypeColors[code.trim().toLowerCase()] ??
    requestTypeColors[name.trim().toLowerCase()] ??
    requestTypeColors.other
}

const approvalProgressColors: Record<string, string> = {
  pending: '#F59E0B',
  approved: '#10B981',
  rejected: '#EF4444',
  cancelled: '#64748B',
  'awaiting routing': '#3B82F6',
  awaiting_routing: '#3B82F6',
}

export function approvalProgressColor(label: string) {
  return approvalProgressColors[label.trim().toLowerCase()] ?? '#64748B'
}

const priorityColors: Record<string, string> = {
  low: '#10B981',
  medium: '#0D9488',
  high: '#F59E0B',
  urgent: '#EF4444',
  unspecified: '#64748B',
}

export function priorityColor(label: string) {
  return priorityColors[label.trim().toLowerCase()] ?? '#64748B'
}
