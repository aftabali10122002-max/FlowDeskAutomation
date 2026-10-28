/* @vitest-environment jsdom */
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  getRequestAttachments,
  removeRequestAttachment,
  uploadRequestAttachment,
} from './api'
import {
  AttachmentPicker,
  RequestAttachmentsPanel,
} from './RequestAttachments'

vi.mock('./api', () => ({
  downloadRequestAttachment: vi.fn(),
  getRequestAttachments: vi.fn(),
  removeRequestAttachment: vi.fn(),
  uploadRequestAttachment: vi.fn(),
}))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  vi.unstubAllGlobals()
})

describe('request attachments', () => {
  it('rejects unsupported files before upload', async () => {
    const onChange = vi.fn()
    render(<AttachmentPicker files={[]} onChange={onChange} />)

    fireEvent.change(screen.getByLabelText('Choose supporting documents'), {
      target: {
        files: [new File(['bad'], 'script.exe', { type: 'application/octet-stream' })],
      },
    })

    expect((await screen.findByRole('alert')).textContent).toContain(
      'Allowed files are PDF, JPEG, PNG, DOCX, and XLSX.',
    )
    expect(onChange).not.toHaveBeenCalled()
  })

  it('reuses upload identifiers when retrying after list refresh fails', async () => {
    vi.stubGlobal('crypto', {
      randomUUID: vi.fn().mockReturnValue('stable-upload-id'),
    })
    vi.mocked(getRequestAttachments)
      .mockResolvedValueOnce([])
      .mockRejectedValueOnce(new Error('Refresh failed'))
      .mockResolvedValueOnce([])
    vi.mocked(uploadRequestAttachment).mockResolvedValue({
      id: 8,
      requestId: 7,
      fileName: 'receipt.pdf',
      extension: 'pdf',
      mediaType: 'application/pdf',
      sizeBytes: 4,
      uploadedAtUtc: '2026-10-19T12:00:00Z',
      uploadedBy: 'Employee',
      scanStatus: 'NotScanned',
      isAvailable: false,
    })
    const user = userEvent.setup()
    render(
      <RequestAttachmentsPanel
        requestId={7}
        canUpload
        canRemove={false}
      />,
    )

    await screen.findByText('No attachments have been added.')
    const file = new File(['%PDF'], 'receipt.pdf', { type: 'application/pdf' })
    fireEvent.change(screen.getByLabelText('Choose supporting documents'), {
      target: { files: [file] },
    })
    await user.click(screen.getByRole('button', { name: 'Upload attachments' }))
    expect(await screen.findByText('Refresh failed')).toBeTruthy()
    const uploadButton = screen.getByRole('button', { name: 'Upload attachments' })
    await waitFor(() => {
      expect((uploadButton as HTMLButtonElement).disabled).toBe(false)
    })
    await user.click(uploadButton)

    await waitFor(() => {
      expect(uploadRequestAttachment).toHaveBeenCalledTimes(2)
    })
    expect(vi.mocked(uploadRequestAttachment).mock.calls[0][3]).toBe(
      'stable-upload-id',
    )
    expect(vi.mocked(uploadRequestAttachment).mock.calls[1][3]).toBe(
      'stable-upload-id',
    )
  })

  it('requires confirmation before removing an attachment', async () => {
    vi.mocked(getRequestAttachments).mockResolvedValue([{
      id: 12,
      requestId: 7,
      fileName: 'receipt.pdf',
      extension: 'pdf',
      mediaType: 'application/pdf',
      sizeBytes: 1024,
      uploadedAtUtc: '2026-10-19T12:00:00Z',
      uploadedBy: 'Employee',
      scanStatus: 'NotScanned',
      isAvailable: false,
    }])
    vi.mocked(removeRequestAttachment).mockResolvedValue()
    const user = userEvent.setup()
    render(
      <RequestAttachmentsPanel
        requestId={7}
        canUpload={false}
        canRemove
      />,
    )

    await screen.findByText('receipt.pdf')
    await user.click(screen.getByRole('button', { name: 'Remove' }))
    expect(screen.getByRole('alertdialog')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Confirm' }))

    await waitFor(() => expect(removeRequestAttachment).toHaveBeenCalledWith(7, 12))
    expect(screen.queryByText('receipt.pdf')).toBeNull()
  })
})
