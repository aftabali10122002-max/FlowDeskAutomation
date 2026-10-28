/* @vitest-environment jsdom */
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Reports } from './Reports'

const reportApi = vi.hoisted(() => ({
  getApprovalHistoryReport: vi.fn(),
  getLeaveReport: vi.fn(),
  getReportFilterOptions: vi.fn(),
  getRequestReport: vi.fn(),
  getWorkflowPerformanceReport: vi.fn(),
  downloadReportExport: vi.fn(),
}))

vi.mock('./api', () => reportApi)

const reportPage = {
  items: [],
  totalCount: 45,
  page: 1,
  pageSize: 20,
}

describe('Reports exports', () => {
  beforeEach(() => {
    reportApi.getApprovalHistoryReport.mockResolvedValue(reportPage)
    reportApi.getLeaveReport.mockResolvedValue(reportPage)
    reportApi.getReportFilterOptions.mockResolvedValue({
      departments: [{ id: 7, name: 'Operations' }],
      employees: [{ id: 9, name: 'Jordan Employee' }],
      managers: [{ id: 11, name: 'Morgan Manager' }],
      requestTypes: [{ id: 3, name: 'Travel' }],
    })
    reportApi.getRequestReport.mockResolvedValue(reportPage)
    reportApi.getWorkflowPerformanceReport.mockResolvedValue(reportPage)
    reportApi.downloadReportExport.mockResolvedValue({
      blob: new Blob(['report']),
      fileName: 'FlowDesk_Requests_Report.xlsx',
    })
    sessionStorage.setItem('flowdesk-token', 'test-token')
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:report'),
    })
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    })
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
  })

  afterEach(() => {
    cleanup()
    sessionStorage.clear()
    vi.restoreAllMocks()
    vi.clearAllMocks()
  })

  it('exports all current request filters and downloads the named Excel file', async () => {
    render(<Reports role="Admin" />)
    await screen.findByText('Page 1 of 3 · 45 records')

    fireEvent.change(screen.getByLabelText('Department'), {
      target: { value: '7' },
    })
    fireEvent.change(screen.getByLabelText('Request type'), {
      target: { value: '3' },
    })
    fireEvent.change(screen.getByLabelText('Status'), {
      target: { value: 'Approved' },
    })
    fireEvent.change(screen.getByLabelText('Priority'), {
      target: { value: 'High' },
    })
    fireEvent.change(screen.getByLabelText('Employee'), {
      target: { value: '9' },
    })
    fireEvent.change(screen.getByLabelText('Manager'), {
      target: { value: '11' },
    })
    fireEvent.click(screen.getByRole('button', { name: /Submitted/ }))
    await waitFor(() => {
      expect(reportApi.getRequestReport).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 1, sortDirection: 'asc' }),
      )
    })
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => {
      expect(reportApi.getRequestReport).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 2 }),
      )
    })

    fireEvent.click(screen.getByRole('button', { name: 'Export Excel' }))
    await waitFor(() => {
      expect(reportApi.downloadReportExport).toHaveBeenCalledWith(
        'requests',
        expect.objectContaining({
          page: 2,
          sortBy: 'createdAtUtc',
          sortDirection: 'asc',
          departmentId: 7,
          requestTypeId: 3,
          status: 'Approved',
          priority: 'High',
          employeeUserId: 9,
          managerUserId: 11,
        }),
        'xlsx',
      )
    })
    expect(URL.createObjectURL).toHaveBeenCalledOnce()
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalledOnce()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:report')
  })

  it('sends PDF exports for the selected report and disables export buttons while loading', async () => {
    let finishExport: ((file: { blob: Blob; fileName: string }) => void) | undefined
    reportApi.downloadReportExport.mockImplementation(() =>
      new Promise((resolve) => {
        finishExport = resolve
      }))
    render(<Reports role="Manager" />)
    await screen.findByText('Page 1 of 3 · 45 records')
    fireEvent.click(screen.getByRole('tab', { name: 'Workflow Performance' }))
    await screen.findByText('Page 1 of 3 · 45 records')
    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }))

    expect(await screen.findByRole('button', { name: 'Preparing PDF…' }))
      .toHaveProperty('disabled', true)
    expect(screen.getByRole('button', { name: 'Export Excel' }))
      .toHaveProperty('disabled', true)
    expect(reportApi.downloadReportExport).toHaveBeenCalledWith(
      'workflow-performance',
      expect.objectContaining({ from: expect.any(String), to: expect.any(String) }),
      'pdf',
    )

    await act(async () => {
      finishExport?.({ blob: new Blob(['pdf']), fileName: 'FlowDesk_Workflow.pdf' })
    })
    expect(screen.getByRole('button', { name: 'Export PDF' }))
      .toHaveProperty('disabled', false)
  })

  it('shows export errors and re-enables actions', async () => {
    reportApi.downloadReportExport.mockRejectedValue(new Error('Export limit reached.'))
    render(<Reports role="Employee" />)
    await screen.findByText('Page 1 of 3 · 45 records')
    fireEvent.click(screen.getByRole('button', { name: 'Export PDF' }))

    expect((await screen.findByRole('alert')).textContent)
      .toContain('Export limit reached.')
    expect(screen.getByRole('button', { name: 'Export PDF' }))
      .toHaveProperty('disabled', false)
  })
})
