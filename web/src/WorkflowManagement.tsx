import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import {
  activateWorkflow,
  createWorkflowRule,
  createWorkflowVersion,
  deactivateWorkflow,
  getAdminRequestTypes,
  getWorkflowRules,
  processWorkflowEscalations,
  updateWorkflowDraft,
  type Department,
  type User,
  type WorkflowRule,
  type WorkflowRuleInput,
  type WorkflowRuleStage,
  type RequestType,
} from './api'
import './WorkflowManagement.css'

type Props = {
  departments: Department[]
  users: User[]
}

const blankStage = (sequence: number): WorkflowRuleStage => ({
  sequence,
  approverType: 'RequestDepartmentManager',
  approverDepartmentId: null,
  specificApproverUserId: null,
  deadlineMinutes: null,
  escalationAfterMinutes: null,
  escalationApproverUserId: null,
})

const blankInput = (requestTypeId: number | null = null): WorkflowRuleInput => ({
  workflowKey: '',
  name: '',
  requestTypeId,
  departmentId: null,
  minimumAmount: 0,
  maximumAmountExclusive: null,
  priority: null,
  stages: [blankStage(1)],
})

function fromRule(rule: WorkflowRule): WorkflowRuleInput {
  return {
    workflowKey: rule.workflowKey,
    name: rule.name,
    requestTypeId: rule.requestTypeId ?? null,
    departmentId: rule.departmentId,
    minimumAmount: rule.minimumAmount,
    maximumAmountExclusive: rule.maximumAmountExclusive,
    priority: rule.priority,
    stages: rule.stages.map((stage) => ({ ...stage })),
  }
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Workflow operation failed.'
}

export function WorkflowManagement({ departments, users }: Props) {
  const [rules, setRules] = useState<WorkflowRule[]>([])
  const [requestTypes, setRequestTypes] = useState<RequestType[]>([])
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [input, setInput] = useState<WorkflowRuleInput>(blankInput)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const managers = users.filter((user) =>
    user.isActive !== false && user.role.toLowerCase() === 'manager')
  const selected = rules.find((rule) => rule.id === selectedId) ?? null
  const isDraft = selected === null || selected.status === 'Draft'

  const refresh = useCallback(async (preferredId?: number) => {
    setLoading(true)
    try {
      const [data, availableTypes] = await Promise.all([
        getWorkflowRules(),
        getAdminRequestTypes(),
      ])
      setError('')
      setRules(data)
      setRequestTypes(availableTypes)
      const nextId = preferredId && data.some((item) => item.id === preferredId)
        ? preferredId
        : selectedId && data.some((item) => item.id === selectedId)
          ? selectedId
          : data[0]?.id ?? null
      setSelectedId(nextId)
      const nextRule = data.find((item) => item.id === nextId)
      setInput(nextRule
        ? fromRule(nextRule)
        : blankInput(availableTypes.find((type) => type.isEnabled)?.id ?? null))
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setLoading(false)
    }
  }, [selectedId])

  useEffect(() => {
    let active = true
    async function loadInitialRules() {
      try {
        const [data, availableTypes] = await Promise.all([
          getWorkflowRules(),
          getAdminRequestTypes(),
        ])
        if (!active) return
        setRules(data)
        setRequestTypes(availableTypes)
        const firstRule = data[0] ?? null
        setSelectedId(firstRule?.id ?? null)
        setInput(firstRule
          ? fromRule(firstRule)
          : blankInput(availableTypes.find((type) => type.isEnabled)?.id ?? null))
      } catch (caught) {
        if (active) setError(errorMessage(caught))
      } finally {
        if (active) setLoading(false)
      }
    }
    void loadInitialRules()
    return () => { active = false }
  }, [])

  function chooseRule(ruleId: number) {
    setSelectedId(ruleId)
    const selectedRule = rules.find((rule) => rule.id === ruleId)
    setInput(selectedRule ? fromRule(selectedRule) : blankInput())
    setNotice('')
    setError('')
  }

  function updateInput<K extends keyof WorkflowRuleInput>(
    key: K,
    value: WorkflowRuleInput[K],
  ) {
    setInput((current) => ({ ...current, [key]: value }))
  }

  function updateStage(index: number, changes: Partial<WorkflowRuleStage>) {
    setInput((current) => ({
      ...current,
      stages: current.stages.map((stage, stageIndex) =>
        stageIndex === index ? { ...stage, ...changes } : stage),
    }))
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (saving) return
    setSaving(true)
    setError('')
    setNotice('')
    const payload = {
      ...input,
      workflowKey: input.workflowKey.trim(),
      name: input.name.trim(),
      stages: input.stages.map((stage, index) => ({
        ...stage,
        sequence: index + 1,
      })),
    }
    try {
      const saved = selectedId
        ? await updateWorkflowDraft(selectedId, payload)
        : await createWorkflowRule(payload)
      setSelectedId(saved.id)
      setInput(fromRule(saved))
      setNotice(`Draft version ${saved.version} saved.`)
      await refresh(saved.id)
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setSaving(false)
    }
  }

  async function mutateRule(action: 'activate' | 'deactivate' | 'version') {
    if (!selected) return
    setSaving(true)
    setError('')
    setNotice('')
    let preferredId = selected.id
    try {
      if (action === 'activate') {
        await activateWorkflow(selected.id)
        setNotice('Workflow version activated.')
      } else if (action === 'deactivate') {
        await deactivateWorkflow(selected.id)
        setNotice('Workflow deactivated for new requests.')
      } else {
        const draft = await createWorkflowVersion(selected.id)
        setSelectedId(draft.id)
        setInput(fromRule(draft))
        preferredId = draft.id
        setNotice(`Draft version ${draft.version} created.`)
      }
      await refresh(preferredId)
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setSaving(false)
    }
  }

  async function runEscalations() {
    setSaving(true)
    setError('')
    setNotice('')
    try {
      const result = await processWorkflowEscalations()
      setNotice(`${result.processed} overdue stage(s) escalated.`)
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="workflow-management">
      <header className="workflow-heading">
        <div>
          <div className="eyebrow">WORKSPACE · ADMIN</div>
          <h1>Approval workflows</h1>
          <p className="page-subtitle">Versioned routing rules for new approval requests.</p>
        </div>
        <div className="workflow-heading-actions">
          <button className="button button-secondary" onClick={() => void runEscalations()} disabled={saving}>
            Process overdue stages
          </button>
          <button className="button button-primary" onClick={() => { setSelectedId(null); setInput(blankInput(requestTypes.find((type) => type.isEnabled)?.id ?? null)); setNotice(''); setError('') }}>
            <span className="button-plus">+</span> New workflow
          </button>
        </div>
      </header>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      <div className="workflow-layout">
        <aside className="workflow-rule-list content-card">
          <div className="workflow-list-heading">
            <div><strong>Rules and versions</strong><span>{rules.length} versions</span></div>
            <button className="text-button" onClick={() => void refresh()} disabled={loading}>Refresh</button>
          </div>
          {loading ? <div className="workflow-empty">Loading rules…</div> : rules.length === 0
            ? <div className="workflow-empty">No rules configured yet.</div>
            : rules.map((rule) => (
              <button
                className={`workflow-rule-option ${selectedId === rule.id ? 'selected' : ''}`}
                key={rule.id}
                onClick={() => chooseRule(rule.id)}
              >
                <span className={`workflow-status status-${rule.status.toLowerCase()}`}>{rule.status}</span>
                <strong>{rule.name}</strong>
                <small>{rule.workflowKey} · v{rule.version} · {rule.requestTypeName ?? 'Legacy generic'}</small>
              </button>
            ))}
        </aside>
        <section className="content-card workflow-editor">
          <div className="workflow-editor-heading">
            <div>
              <div className="section-eyebrow">{selected ? `${selected.workflowKey} · VERSION ${selected.version}` : 'NEW RULE'}</div>
              <h2>{selected ? selected.name : 'Create a workflow rule'}</h2>
            </div>
            {selected && <span className={`workflow-status status-${selected.status.toLowerCase()}`}>{selected.status}</span>}
          </div>
          <form onSubmit={(event) => { void save(event) }}>
            <div className="workflow-form-grid">
              <label>Workflow key <span className="required-star" aria-hidden="true"></span><input required maxLength={100} value={input.workflowKey} onChange={(event) => updateInput('workflowKey', event.target.value)} disabled={Boolean(selected && !isDraft)} placeholder="e.g. department-expense" /></label>
              <label>Display name <span className="required-star" aria-hidden="true"></span><input required maxLength={150} value={input.name} onChange={(event) => updateInput('name', event.target.value)} disabled={Boolean(selected && !isDraft)} placeholder="Department expense approvals" /></label>
              <label>Request type<select value={input.requestTypeId ?? ''} onChange={(event) => {
                const requestTypeId = event.target.value ? Number(event.target.value) : null
                const requestType = requestTypes.find((type) => type.id === requestTypeId)
                setInput((current) => ({
                  ...current,
                  requestTypeId,
                  ...(!requestType?.requiresAmount
                    ? { minimumAmount: 0, maximumAmountExclusive: null }
                    : {}),
                }))
              }} disabled={Boolean(selected && !isDraft)}><option value="">Legacy generic requests</option>{requestTypes.map((type) => <option key={type.id} value={type.id} disabled={!type.isEnabled && input.requestTypeId !== type.id}>{type.displayName}{type.isEnabled ? '' : ' · disabled'}</option>)}</select></label>
              <label>Department scope<select value={input.departmentId ?? ''} onChange={(event) => updateInput('departmentId', event.target.value ? Number(event.target.value) : null)} disabled={Boolean(selected && !isDraft)}><option value="">All active departments</option>{departments.filter((department) => department.isActive).map((department) => <option key={department.id} value={department.id}>{department.name}</option>)}</select></label>
              <label>Priority scope<select value={input.priority ?? ''} onChange={(event) => updateInput('priority', (event.target.value || null) as WorkflowRuleInput['priority'])} disabled={Boolean(selected && !isDraft)}><option value="">Any priority</option><option>Low</option><option>Medium</option><option>High</option><option>Urgent</option></select></label>
              {(input.requestTypeId == null || requestTypes.find((type) => type.id === input.requestTypeId)?.requiresAmount) && <>
                <label>Minimum amount <span className="required-star" aria-hidden="true"></span><input type="number" min="0" step="0.01" required value={input.minimumAmount} onChange={(event) => updateInput('minimumAmount', Number(event.target.value))} disabled={Boolean(selected && !isDraft)} /></label>
                <label>Maximum amount (exclusive)<input type="number" min="0.01" step="0.01" value={input.maximumAmountExclusive ?? ''} onChange={(event) => updateInput('maximumAmountExclusive', event.target.value ? Number(event.target.value) : null)} disabled={Boolean(selected && !isDraft)} placeholder="No upper limit" /></label>
              </>}
            </div>
            <div className="workflow-stages-heading">
              <div><h3>Sequential approval stages</h3><p>Only one stage is active at a time. Escalation is timed from activation.</p></div>
              {isDraft && <button type="button" className="button button-secondary" disabled={input.stages.length >= 10} onClick={() => updateInput('stages', [...input.stages, blankStage(input.stages.length + 1)])}>Add stage</button>}
            </div>
            <div className="workflow-stages">
              {input.stages.map((stage, index) => (
                <article className="workflow-stage-editor" key={index}>
                  <div className="workflow-stage-number">{index + 1}</div>
                  <div className="workflow-stage-fields">
                    <label>Approver type<select value={stage.approverType} onChange={(event) => updateStage(index, {
                      approverType: event.target.value as WorkflowRuleStage['approverType'],
                      approverDepartmentId: null,
                      specificApproverUserId: null,
                    })} disabled={!isDraft}><option value="RequestDepartmentManager">Request department Manager</option><option value="DepartmentManager">Manager in selected department</option><option value="SpecificManager">Specific Manager</option></select></label>
                    {stage.approverType === 'DepartmentManager' && <label>Approver department <span className="required-star" aria-hidden="true"></span><select required value={stage.approverDepartmentId ?? ''} onChange={(event) => updateStage(index, { approverDepartmentId: event.target.value ? Number(event.target.value) : null })} disabled={!isDraft}><option value="">Select department</option>{departments.filter((department) => department.isActive).map((department) => <option key={department.id} value={department.id}>{department.name}</option>)}</select></label>}
                    {stage.approverType === 'SpecificManager' && <label>Manager <span className="required-star" aria-hidden="true"></span><select required value={stage.specificApproverUserId ?? ''} onChange={(event) => updateStage(index, { specificApproverUserId: event.target.value ? Number(event.target.value) : null })} disabled={!isDraft}><option value="">Select Manager</option>{managers.map((manager) => <option key={manager.id} value={manager.id}>{manager.fullName} · {departments.find((department) => department.id === manager.departmentId)?.name ?? 'No department'}</option>)}</select></label>}
                    <label>Deadline (minutes)<input type="number" min="1" value={stage.deadlineMinutes ?? ''} onChange={(event) => {
                      const deadlineMinutes = event.target.value ? Number(event.target.value) : null
                      updateStage(index, {
                        deadlineMinutes,
                        escalationAfterMinutes: deadlineMinutes && stage.escalationAfterMinutes && stage.escalationAfterMinutes < deadlineMinutes
                          ? deadlineMinutes
                          : stage.escalationAfterMinutes,
                      })
                    }} disabled={!isDraft} placeholder="No deadline" /></label>
                    <label>Escalate after (minutes)<input type="number" min={stage.deadlineMinutes ?? 1} value={stage.escalationAfterMinutes ?? ''} onChange={(event) => updateStage(index, { escalationAfterMinutes: event.target.value ? Number(event.target.value) : null })} disabled={!isDraft} placeholder="Disabled" /></label>
                    {stage.escalationAfterMinutes != null && <label>Escalation recipient <span className="required-star" aria-hidden="true"></span><select required value={stage.escalationApproverUserId ?? ''} onChange={(event) => updateStage(index, { escalationApproverUserId: event.target.value ? Number(event.target.value) : null })} disabled={!isDraft}><option value="">Select Manager</option>{managers.map((manager) => <option key={manager.id} value={manager.id}>{manager.fullName}</option>)}</select></label>}
                  </div>
                  {isDraft && input.stages.length > 1 && <button type="button" className="workflow-remove-stage" aria-label={`Remove stage ${index + 1}`} onClick={() => updateInput('stages', input.stages.filter((_, stageIndex) => stageIndex !== index).map((item, stageIndex) => ({ ...item, sequence: stageIndex + 1 })))}>×</button>}
                </article>
              ))}
            </div>
            <footer className="workflow-form-actions">
              {isDraft && <button className="button button-primary" disabled={saving}>{saving ? 'Saving…' : selected ? 'Save draft' : 'Create draft'}</button>}
              {selected?.status === 'Draft' && <button type="button" className="button button-secondary" onClick={() => void mutateRule('activate')} disabled={saving}>Publish version</button>}
              {selected?.status === 'Active' && <button type="button" className="button button-secondary" onClick={() => void mutateRule('deactivate')} disabled={saving}>Deactivate</button>}
              {selected && selected.status !== 'Draft' && <button type="button" className="button button-secondary" onClick={() => void mutateRule('version')} disabled={saving}>Create next draft version</button>}
            </footer>
          </form>
        </section>
      </div>
    </section>
  )
}
