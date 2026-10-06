import {
  ArrivalWindow,
  JobType,
  MAX_TASKS,
  MaterialSource,
  Priority,
  RecurrenceFrequency,
  TASK_LIMIT_MESSAGE,
  WorkOrderBody,
  WorkOrderCommunication,
} from '../../jobs/models/work-order.model';
import { hasAtMostDecimals } from './quote-lines';

/** A task in the editor: the contract `label` plus a client-only `uid` that survives reorders. */
export interface TaskRow {
  readonly uid: string;
  readonly label: string;
}

/** A planned material; the quantity may be blank while typing. */
export interface MaterialRow {
  readonly uid: string;
  readonly quoteLineId: string | null;
  readonly catalogItemId: string | null;
  readonly description: string;
  readonly quantity: number | null;
  readonly unit: string;
  readonly source: MaterialSource;
}

export interface WorkOrderForm {
  readonly title: string;
  readonly jobType: JobType;
  readonly serviceCategoryId: string | null;
  readonly branchId: string | null;
  readonly priority: Priority;
  readonly durationMinutes: number | null;
  readonly skillIds: readonly string[];
  readonly tasks: readonly TaskRow[];
  readonly materials: readonly MaterialRow[];
  readonly instructions: string;
  readonly preferredDate: string;
  readonly arrivalWindow: ArrivalWindow;
  readonly frequency: RecurrenceFrequency | null;
  readonly occurrences: number | null;
  readonly communication: WorkOrderCommunication;
}

export type MaterialField = 'description' | 'quantity' | 'unit' | 'source';
export type MaterialErrors = Partial<Record<MaterialField, string>>;

export const MAX_MATERIALS = 50;
export const MAX_SKILLS = 10;
export const INSTRUCTIONS_LIMIT = 2000;
const MAX_QUANTITY = 99_999.999;
const SOURCES: readonly string[] = ['truck_stock', 'warehouse', 'to_purchase'];

export function formFromValues(values: WorkOrderBody, uid: () => string): WorkOrderForm {
  return {
    title: values.title,
    jobType: values.jobType,
    serviceCategoryId: values.serviceCategoryId,
    branchId: values.branchId,
    priority: values.priority,
    durationMinutes: values.estimatedDurationMinutes,
    skillIds: [...values.skillIds],
    tasks: values.tasks.map((task) => ({ uid: uid(), label: task.label })),
    materials: values.materials.map((material) => ({
      uid: uid(),
      quoteLineId: material.quoteLineId ?? null,
      catalogItemId: material.catalogItemId ?? null,
      description: material.description,
      quantity: material.quantity,
      unit: material.unit,
      source: material.source,
    })),
    instructions: values.instructions ?? '',
    preferredDate: values.preferredDate ?? '',
    arrivalWindow: values.arrivalWindow,
    frequency: values.recurrence?.frequency ?? null,
    occurrences: values.recurrence?.count ?? null,
    communication: { ...values.communication },
  };
}

/** The request body: client-only fields stripped, texts trimmed, empty texts as null. */
export function toBody(form: WorkOrderForm): WorkOrderBody {
  const instructions = form.instructions.trim();
  const preferredDate = form.preferredDate === '' ? null : form.preferredDate;
  return {
    title: form.title.trim(),
    jobType: form.jobType,
    serviceCategoryId: form.serviceCategoryId,
    branchId: form.branchId,
    priority: form.priority,
    estimatedDurationMinutes: form.durationMinutes,
    skillIds: form.skillIds,
    tasks: form.tasks.map((task) => ({ label: task.label.trim() })),
    materials: form.materials.map((material) => ({
      quoteLineId: material.quoteLineId,
      catalogItemId: material.catalogItemId,
      description: material.description.trim(),
      quantity: material.quantity ?? 0,
      unit: material.unit.trim(),
      source: material.source,
    })),
    instructions: instructions.length > 0 ? instructions : null,
    preferredDate,
    arrivalWindow: preferredDate === null ? 'any' : form.arrivalWindow,
    recurrence:
      form.jobType === 'recurring' && form.frequency !== null && form.occurrences !== null
        ? { frequency: form.frequency, count: form.occurrences }
        : null,
    communication: form.communication,
  };
}

/** BR-08 limits and messages for one material; empty when valid. */
export function materialErrors(material: Omit<MaterialRow, 'uid'>): MaterialErrors {
  const errors: Record<string, string> = {};
  const description = material.description.trim();
  if (description.length === 0 || description.length > 240) {
    errors['description'] = 'Enter a material.';
  }
  const quantity = material.quantity;
  if (
    quantity === null ||
    !Number.isFinite(quantity) ||
    quantity <= 0 ||
    quantity > MAX_QUANTITY ||
    !hasAtMostDecimals(quantity, 3)
  ) {
    errors['quantity'] = 'Enter a quantity greater than 0.';
  }
  const unit = material.unit.trim();
  if (unit.length === 0 || unit.length > 40) {
    errors['unit'] = 'Enter a unit.';
  }
  if (!SOURCES.includes(material.source)) {
    errors['source'] = 'Select a source.';
  }
  return errors;
}

/**
 * Field errors of the whole form (BR-08), keyed by field. Tasks use `tasks[<uid>]` and materials
 * `materials[<uid>]`; `today` is the current date in the selected branch's time zone (BR-14).
 */
export function formErrors(form: WorkOrderForm, today: string): Record<string, string> {
  const errors: Record<string, string> = {};
  const title = form.title.trim();
  if (title.length === 0) {
    errors['title'] = 'Enter a work order title.';
  } else if (title.length > 160) {
    errors['title'] = 'Title must be 160 characters or fewer.';
  }
  if (form.serviceCategoryId === null) {
    errors['serviceCategoryId'] = 'Select a service category.';
  }
  if (form.branchId === null) {
    errors['branchId'] = 'Select a branch.';
  }
  if (form.skillIds.length > MAX_SKILLS) {
    errors['skillIds'] = 'Select up to 10 skills.';
  }
  if (form.tasks.length === 0) {
    errors['tasks'] = 'Add at least one task.';
  } else if (form.tasks.length > MAX_TASKS) {
    errors['tasks'] = TASK_LIMIT_MESSAGE;
  }
  for (const task of form.tasks) {
    const label = task.label.trim();
    if (label.length === 0) {
      errors[`tasks[${task.uid}]`] = 'Enter a task.';
    } else if (label.length > 240) {
      errors[`tasks[${task.uid}]`] = 'Task must be 240 characters or fewer.';
    }
  }
  if (form.materials.length > MAX_MATERIALS) {
    errors['materials'] = 'A work order can have up to 50 materials.';
  }
  for (const material of form.materials) {
    const first = Object.values(materialErrors(material))[0];
    if (first !== undefined) {
      errors[`materials[${material.uid}]`] = first;
    }
  }
  if (form.instructions.trim().length > INSTRUCTIONS_LIMIT) {
    errors['instructions'] = 'Instructions must be 2000 characters or fewer.';
  }
  if (form.preferredDate !== '' && !(form.preferredDate >= today)) {
    errors['preferredDate'] = 'Select today or a later date.';
  }
  if (form.jobType === 'recurring') {
    if (form.frequency === null) {
      errors['recurrence'] = 'Select how often this job repeats.';
    } else if (
      form.occurrences === null ||
      !Number.isInteger(form.occurrences) ||
      form.occurrences < 2 ||
      form.occurrences > 24
    ) {
      errors['recurrence'] = 'Enter between 2 and 24 occurrences.';
    }
  }
  return errors;
}

export function taskName(task: { readonly label: string }, index: number): string {
  const label = task.label.trim();
  return label.length > 0 ? label : `Task ${index + 1}`;
}

export interface TaskMove {
  readonly tasks: TaskRow[];
  /** Position (1-based) of the moved task and the list size, for the announcement. */
  readonly position: number;
  readonly count: number;
  readonly task: TaskRow;
}

/** Moves the task at `from` to `to`; `null` for an out-of-range or no-op move. Used by the drag-and-drop reorder. */
export function moveTask(tasks: readonly TaskRow[], from: number, to: number): TaskMove | null {
  const task = tasks[from];
  if (task === undefined || tasks[to] === undefined || from === to) {
    return null;
  }
  const next = [...tasks];
  next.splice(from, 1);
  next.splice(to, 0, task);
  return { tasks: next, position: to + 1, count: next.length, task };
}

/** BR-11: appends template labels after the tasks; `null` when the result would exceed 50. */
export function appendTemplateTasks(
  tasks: readonly TaskRow[],
  labels: readonly string[],
  uid: () => string,
): TaskRow[] | null {
  if (tasks.length + labels.length > MAX_TASKS) {
    return null;
  }
  return [...tasks, ...labels.map((label) => ({ uid: uid(), label }))];
}

/** Active templates of the selected category first, then the rest, each alphabetical (BR-11). */
export function sortTemplates<
  T extends { readonly name: string; readonly serviceCategoryId: string | null },
>(templates: readonly T[], categoryId: string | null): T[] {
  const byName = (a: T, b: T): number =>
    a.name.localeCompare(b.name, 'en', { sensitivity: 'base' });
  const matching = templates.filter(
    (template) => categoryId !== null && template.serviceCategoryId === categoryId,
  );
  const rest = templates.filter((template) => !matching.includes(template));
  return [...matching.sort(byName), ...rest.sort(byName)];
}

/** Maps a server field path (`tasks[2].label`, `materials[0].quantity`, `recurrence.count`) to a form key. */
export function errorKey(path: string, form: WorkOrderForm): string | null {
  const match = /^(\w+)(?:\[(\d+)\])?/.exec(path);
  if (match === null) {
    return null;
  }
  const root = match[1].charAt(0).toLowerCase() + match[1].slice(1);
  const index = match[2] === undefined ? null : Number(match[2]);
  if (index !== null && (root === 'tasks' || root === 'materials')) {
    const row = (root === 'tasks' ? form.tasks : form.materials)[index];
    return row === undefined ? root : `${root}[${row.uid}]`;
  }
  const known = [
    'title',
    'jobType',
    'serviceCategoryId',
    'branchId',
    'priority',
    'estimatedDurationMinutes',
    'skillIds',
    'tasks',
    'materials',
    'instructions',
    'preferredDate',
    'arrivalWindow',
    'recurrence',
  ];
  return known.includes(root) ? root : null;
}
