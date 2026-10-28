import { useEffect, useRef } from 'react'
import type { RefObject } from 'react'

export function useDialogEscape(
  dialogRef: RefObject<HTMLElement | null>,
  onEscape: () => void,
  canEscape = true,
) {
  const onEscapeRef = useRef(onEscape)
  const canEscapeRef = useRef(canEscape)

  useEffect(() => {
    onEscapeRef.current = onEscape
  }, [onEscape])

  useEffect(() => {
    canEscapeRef.current = canEscape
  }, [canEscape])

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null
    const firstFocusable = dialogRef.current?.querySelector<HTMLElement>(
      'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])',
    )
    firstFocusable?.focus()

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') return
      const dialogs = document.querySelectorAll<HTMLElement>('[role="dialog"]')
      if (
        dialogs.item(dialogs.length - 1) !== dialogRef.current ||
        !canEscapeRef.current
      ) return
      event.preventDefault()
      onEscapeRef.current()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      previousFocus?.focus()
    }
  }, [dialogRef])
}
