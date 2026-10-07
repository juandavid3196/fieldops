export type JobType = 'one_time' | 'recurring';
export type Priority = 'urgent' | 'high' | 'normal' | 'low';
export type WorkOrderStatus = 'draft' | 'ready_to_schedule' | 'scheduled';
export type MaterialSource = 'truck_stock' | 'warehouse' | 'to_purchase';
export type ArrivalWindow = 'any' | '08-11' | '09-12' | '12-15' | '13-16' | '15-18';
export type RecurrenceFrequency = 'weekly' | 'biweekly' | 'monthly' | 'quarterly';

/** UX only (BR-01): Manage roles of the work order endpoints; the backend decides. */
export const WORK_ORDER_MANAGE_ROLES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
];

/** Fixed copy (backend titles are never displayed). */
export const WO_LOAD_ERROR_MESSAGE = "We couldn't load this work order. Try again.";
export const WO_NOT_APPROVED_MESSAGE = 'This quote is no longer approved.';
export const WO_CUSTOMER_REQUIRED_MESSAGE =
  'Link a customer and property to this request before creating a work order.';
export const WO_NOT_AVAILABLE_MESSAGE = "This work order isn't available.";
export const WO_FORBIDDEN_MESSAGE = "You don't have access to work orders.";
export const WO_CHANGED_MESSAGE =
  'This work order was changed by someone else. Reload to see the latest version.';
export const WO_REQUEST_CHANGED_MESSAGE = 'The request changed. Reload and try again.';
export const WO_SAVE_FAILED_MESSAGE = "We couldn't save the work order. Try again.";
export const WO_ALREADY_CREATED_MESSAGE = 'This work order has already been created.';
export const JOBS_LOAD_ERROR_MESSAGE = "We couldn't load jobs. Try again.";
export const JOBS_EMPTY_MESSAGE = 'No jobs yet. Jobs are created from approved quotes.';
export const MAX_TASKS = 50;
export const TASK_LIMIT_MESSAGE = 'A work order can have up to 50 tasks.';

export interface WorkOrderMaterialBody {
  readonly quoteLineId?: string | null;
  readonly catalogItemId?: string | null;
  readonly description: string;
  readonly quantity: number;
  readonly unit: string;
  readonly source: MaterialSource;
}

export interface WorkOrderRecurrence {
  readonly frequency: RecurrenceFrequency;
  readonly count: number;
}

export interface WorkOrderCommunication {
  readonly notifyCustomerWhenScheduled: boolean;
  readonly sendTechnicianDetails: boolean;
  readonly sendArrivalReminder: boolean;
}

export interface WorkOrderBody {
  readonly title: string;
  readonly jobType: JobType;
  readonly serviceCategoryId: string | null;
  readonly branchId: string | null;
  readonly priority: Priority;
  readonly estimatedDurationMinutes: number | null;
  readonly skillIds: readonly string[];
  readonly tasks: readonly { readonly label: string }[];
  readonly materials: readonly WorkOrderMaterialBody[];
  readonly instructions: string | null;
  readonly preferredDate: string | null;
  readonly arrivalWindow: ArrivalWindow;
  readonly recurrence: WorkOrderRecurrence | null;
  readonly communication: WorkOrderCommunication;
}

export interface WorkOrderEditor {
  readonly quote: {
    readonly id: string;
    readonly displayNumber: string;
    readonly approvedAt: string;
    readonly approvedTotal: number;
    readonly currency: string;
  };
  readonly requestId: string;
  readonly customer: {
    readonly name: string;
    readonly phone: string | null;
    readonly email: string | null;
    readonly address: string;
  };
  readonly accessNote: string | null;
  readonly assessment: {
    readonly id: string;
    readonly technicianName: string | null;
    readonly diagnosis: string | null;
    readonly photos: readonly { readonly id: string }[];
  } | null;
  readonly workOrder: {
    readonly id: string;
    readonly displayNumber: string;
    readonly status: WorkOrderStatus;
    readonly updatedAt: string;
  } | null;
  readonly values: WorkOrderBody;
  readonly options: {
    readonly branches: {
      readonly id: string;
      readonly name: string;
      readonly timezone: string | null;
    }[];
    readonly categories: { readonly id: string; readonly name: string }[];
    readonly skills: { readonly id: string; readonly name: string }[];
  };
  readonly organizationTimezone: string;
}

export interface WorkOrderCreated {
  readonly id: string;
  readonly displayNumber: string;
}

export interface WorkOrderListItem {
  readonly id: string;
  readonly displayNumber: string;
  readonly title: string;
  readonly customerName: string;
  readonly branchName: string;
  readonly priority: Priority;
  readonly status: WorkOrderStatus;
  readonly createdAt: string;
}

export interface WorkOrderList {
  readonly items: readonly WorkOrderListItem[];
  readonly total: number;
}

export interface WorkOrderDetail {
  readonly id: string;
  readonly displayNumber: string;
  readonly status: WorkOrderStatus;
  readonly updatedAt: string;
  readonly canManage: boolean;
  readonly quote: {
    readonly id: string;
    readonly displayNumber: string;
    readonly approvedTotal: number;
    readonly currency: string;
  };
  readonly customer: { readonly id: string | null; readonly name: string };
  readonly propertyAddress: string | null;
  readonly accessNote: string | null;
  readonly jobType: JobType;
  readonly category: { readonly id: string; readonly name: string };
  readonly branch: { readonly id: string; readonly name: string; readonly timezone: string | null };
  readonly priority: Priority;
  readonly estimatedDurationMinutes: number | null;
  readonly skills: readonly { readonly id: string; readonly name: string }[];
  readonly recurrence: WorkOrderRecurrence | null;
  readonly preferredStart: string | null;
  readonly preferredEnd: string | null;
  readonly arrivalWindow: ArrivalWindow;
  readonly preferredDate: string | null;
  readonly tasks: readonly { readonly label: string }[];
  readonly materials: readonly WorkOrderMaterialBody[];
  readonly instructions: string | null;
  readonly communication: WorkOrderCommunication;
  readonly visits: readonly { readonly visitNumber: number; readonly status: string }[];
}

export interface ChecklistTemplate {
  readonly id: string;
  readonly name: string;
  readonly serviceCategoryId: string | null;
  readonly items: readonly { readonly label: string }[];
}

export interface ChecklistTemplateBody {
  readonly name: string;
  readonly serviceCategoryId: string | null;
  readonly items: readonly { readonly label: string }[];
}
