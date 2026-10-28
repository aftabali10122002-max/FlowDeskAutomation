import { useEffect, useRef, useState } from 'react'
import {
  downloadRequestAttachment,
  getRequestAttachments,
  removeRequestAttachment,
  uploadRequestAttachment,
} from './api'
import type { RequestAttachmentItem } from './api'
import './RequestAttachments.css'

const maximumFiles = 5
const maximumFileBytes = 10 * 1024 * 1024
const maximumTotalBytes = 25 * 1024 * 1024
const allowedExtensions = new Set([
  '.pdf',
  '.jpg',
  '.jpeg',
  '.png',
  '.docx',
  '.xlsx',
])
const acceptedFiles = '.pdf,.jpg,.jpeg,.png,.docx,.xlsx'

export function AttachmentPicker({
  files,
  onChange,
  disabled = false,
  label = 'Supporting documents',
}: {
  files: File[]
  onChange: (files: File[]) => void
  disabled?: boolean
  label?: string
}) {
  const [error, setError] = useState('')
  const [dragging, setDragging] = useState(false)
  const inputRef = useRef<HTMLInputElement | null>(null)

  function addFiles(selected: File[]) {
    setError('')
    if (files.length + selected.length > maximumFiles) {
      setError(`You can attach up to ${maximumFiles} files.`)
      return
    }

    let total = files.reduce((sum, file) => sum + file.size, 0)
    for (const file of selected) {
      const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase()
      if (!allowedExtensions.has(extension)) {
        setError('Allowed files are PDF, JPEG, PNG, DOCX, and XLSX.')
        return
      }
      if (file.size === 0 || file.size > maximumFileBytes) {
        setError('Each file must be non-empty and no larger than 10 MB.')
        return
      }
      total += file.size
      if (total > maximumTotalBytes) {
        setError('Attachments cannot exceed 25 MB per request.')
        return
      }
    }

    const knownFiles = new Set(files.map((file) =>
      `${file.name}:${file.size}:${file.lastModified}`))
    const additions = selected.filter((file) => {
      const key = `${file.name}:${file.size}:${file.lastModified}`
      if (knownFiles.has(key)) return false
      knownFiles.add(key)
      return true
    })
    onChange([...files, ...additions])
  }

  return <section className="attachment-picker" aria-label={label}>
    <div className="attachment-section-heading">
      <div>
        <h3>{label}</h3>
        <p>PDF, JPEG, PNG, DOCX, or XLSX · up to 10 MB each</p>
      </div>
      <span>{files.length}/{maximumFiles}</span>
    </div>
    <div
      className={`attachment-dropzone${dragging ? ' is-dragging' : ''}${disabled ? ' is-disabled' : ''}`}
      onDragEnter={(event) => {
        event.preventDefault()
        if (!disabled) setDragging(true)
      }}
      onDragOver={(event) => event.preventDefault()}
      onDragLeave={(event) => {
        if (event.currentTarget === event.target) setDragging(false)
      }}
      onDrop={(event) => {
        event.preventDefault()
        setDragging(false)
        if (!disabled) addFiles(Array.from(event.dataTransfer.files))
      }}
    >
      <input
        ref={inputRef}
        type="file"
        accept={acceptedFiles}
        multiple
        disabled={disabled}
        aria-label="Choose supporting documents"
        onChange={(event) => {
          addFiles(Array.from(event.currentTarget.files ?? []))
          event.currentTarget.value = ''
        }}
      />
      <strong>Drop files here</strong>
      <span>or</span>
      <button
        type="button"
        className="attachment-choose-button"
        disabled={disabled}
        onClick={() => inputRef.current?.click()}
      >Browse files</button>
      <small>25 MB total per request</small>
    </div>
    {error && <p className="attachment-error" role="alert">{error}</p>}
    {files.length > 0 && <ul className="attachment-file-list" aria-label="Selected files">
      {files.map((file, index) => <li key={`${file.name}:${file.size}:${file.lastModified}`}>
        <span className="attachment-file-icon" aria-hidden="true">FILE</span>
        <span className="attachment-file-copy">
          <strong>{file.name}</strong>
          <small>{formatBytes(file.size)}</small>
        </span>
        <button
          type="button"
          disabled={disabled}
          aria-label={`Remove ${file.name}`}
          onClick={() => onChange(files.filter((_, fileIndex) => fileIndex !== index))}
        >Remove</button>
      </li>)}
    </ul>}
  </section>
}

export function RequestAttachmentsPanel({
  requestId,
  canUpload,
  canRemove,
}: {
  requestId: number
  canUpload: boolean
  canRemove: boolean
}) {
  const [attachments, setAttachments] = useState<RequestAttachmentItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selectedFiles, setSelectedFiles] = useState<File[]>([])
  const [uploading, setUploading] = useState(false)
  const [uploadProgress, setUploadProgress] = useState(0)
  const [removingId, setRemovingId] = useState<number | null>(null)
  const uploadIds = useRef(new WeakMap<File, string>())
  const [preview, setPreview] = useState<{
    attachment: RequestAttachmentItem
    url: string
  } | null>(null)

  useEffect(() => {
    let cancelled = false
    void getRequestAttachments(requestId)
      .then((result) => {
        if (!cancelled) {
          setAttachments(result)
          setError('')
        }
      })
      .catch((caught: unknown) => {
        if (!cancelled) {
          setError(errorMessage(caught))
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [requestId])

  useEffect(() => () => {
    if (preview) URL.revokeObjectURL(preview.url)
  }, [preview])

  async function refresh() {
    setError('')
    const result = await getRequestAttachments(requestId)
    setAttachments(result)
  }

  async function upload() {
    if (!selectedFiles.length || uploading) return
    const total = selectedFiles.reduce((sum, file) => sum + file.size, 0)
    let completed = 0
    setUploading(true)
    setUploadProgress(0)
    setError('')
    try {
      for (const file of selectedFiles) {
        let clientUploadId = uploadIds.current.get(file)
        if (!clientUploadId) {
          clientUploadId = crypto.randomUUID()
          uploadIds.current.set(file, clientUploadId)
        }
        await uploadRequestAttachment(
          requestId,
          file,
          (progress) => {
            const current = completed + file.size * (progress / 100)
            setUploadProgress(Math.round((current / total) * 100))
          },
          clientUploadId,
        )
        completed += file.size
      }
      await refresh()
      setSelectedFiles([])
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setUploading(false)
    }
  }

  async function download(attachment: RequestAttachmentItem) {
    try {
      const blob = await downloadRequestAttachment(
        requestId,
        attachment.id,
      )
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = attachment.fileName
      document.body.appendChild(link)
      link.click()
      link.remove()
      URL.revokeObjectURL(url)
    } catch (caught) {
      setError(errorMessage(caught))
    }
  }

  async function openPreview(attachment: RequestAttachmentItem) {
    try {
      const blob = await downloadRequestAttachment(
        requestId,
        attachment.id,
        true,
      )
      setPreview({
        attachment,
        url: URL.createObjectURL(blob),
      })
    } catch (caught) {
      setError(errorMessage(caught))
    }
  }

  async function remove(attachment: RequestAttachmentItem) {
    setRemovingId(attachment.id)
    setError('')
    try {
      await removeRequestAttachment(requestId, attachment.id)
      setAttachments((current) =>
        current.filter((item) => item.id !== attachment.id))
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setRemovingId(null)
    }
  }

  return <section className="request-attachments" aria-labelledby={`attachments-title-${requestId}`}>
    <div className="attachment-section-heading">
      <div>
        <h3 id={`attachments-title-${requestId}`}>Attachments</h3>
        <p>Supporting documents for this request</p>
      </div>
    </div>
    {error && <p className="attachment-error" role="alert">{error}</p>}
    {loading
      ? <p className="attachment-muted" role="status">Loading attachments…</p>
      : attachments.length === 0
        ? <p className="attachment-muted" role="status">No attachments have been added.</p>
        : <ul className="request-attachment-list">
          {attachments.map((attachment) => <li key={attachment.id}>
            <span className="attachment-file-icon" aria-hidden="true">FILE</span>
            <span className="attachment-file-copy">
              <strong>{attachment.fileName}</strong>
              <small>{formatBytes(attachment.sizeBytes)} · {attachment.uploadedBy}</small>
              {!attachment.isAvailable && <small className="attachment-scan-note">
                {attachment.scanStatus === 'Rejected'
                  ? 'Rejected by malware scanning'
                  : 'Unavailable until an approved scan completes'}
              </small>}
            </span>
            <div className="attachment-actions">
              {attachment.isAvailable &&
                (attachment.mediaType === 'application/pdf' ||
                 attachment.mediaType.startsWith('image/')) &&
                <button
                  type="button"
                  onClick={() => { void openPreview(attachment) }}
                >Preview</button>}
              <button
                type="button"
                disabled={!attachment.isAvailable}
                onClick={() => { void download(attachment) }}
              >Download</button>
              {canRemove && <button
                type="button"
                disabled={removingId === attachment.id}
                onClick={() => setRemovingId(attachment.id)}
              >Remove</button>}
            </div>
            {removingId === attachment.id && canRemove && <div
              className="attachment-confirm"
              role="alertdialog"
              aria-label={`Remove ${attachment.fileName}?`}
            >
              <span>Remove this attachment?</span>
              <button type="button" onClick={() => { void remove(attachment) }}>Confirm</button>
              <button type="button" onClick={() => setRemovingId(null)}>Cancel</button>
            </div>}
          </li>)}
        </ul>}
    {canUpload && <>
      <AttachmentPicker
        files={selectedFiles}
        onChange={setSelectedFiles}
        disabled={uploading}
        label="Add documents"
      />
      {uploading && <div className="attachment-progress" role="status">
        <span>Uploading… {uploadProgress}%</span>
        <progress value={uploadProgress} max="100" aria-label="Upload progress" />
      </div>}
      <button
        type="button"
        className="attachment-upload-button"
        disabled={!selectedFiles.length || uploading}
        onClick={() => { void upload() }}
      >{uploading ? 'Uploading…' : 'Upload attachments'}</button>
    </>}
    {preview && <div
      className="attachment-preview-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) setPreview(null)
      }}
    >
      <section
        className="attachment-preview-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="attachment-preview-title"
      >
        <div className="attachment-preview-heading">
          <h3 id="attachment-preview-title">{preview.attachment.fileName}</h3>
          <button
            type="button"
            aria-label="Close preview"
            onClick={() => setPreview(null)}
          >Close</button>
        </div>
        {preview.attachment.mediaType.startsWith('image/')
          ? <img src={preview.url} alt={preview.attachment.fileName} />
          : <iframe
            src={preview.url}
            title={`Preview of ${preview.attachment.fileName}`}
            sandbox=""
          />}
      </section>
    </div>}
  </section>
}

function formatBytes(bytes: number) {
  return bytes >= 1024 * 1024
    ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
    : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

function errorMessage(error: unknown) {
  return error instanceof Error
    ? error.message
    : 'The attachment operation failed. Please try again.'
}
