import { Observable } from 'rxjs';

export interface ServiceRequestFormService {
  readonly id: string;
  readonly name: string;
}

export interface ServiceRequestFormCategory {
  readonly id: string;
  readonly name: string;
  readonly services: readonly ServiceRequestFormService[];
}

/** `GET /public/organizations/{slug}/service-request-form`. */
export interface ServiceRequestForm {
  readonly organizationName: string;
  readonly phone: string | null;
  readonly website: string | null;
  readonly requestPrefix: string;
  readonly timezone: string;
  readonly categories: readonly ServiceRequestFormCategory[];
}

export type WizardStep = 'contact' | 'property' | 'service' | 'availability' | 'review';
export type PropertyType = 'home' | 'business';
export type Urgency = 'standard' | 'urgent' | 'emergency';
export type DateMode = 'asap' | 'date' | 'flexible';
export type TimeWindow = 'morning' | 'afternoon' | 'evening' | 'any';

export interface ContactData {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  readonly phone: string;
  readonly prefersEmail: boolean;
  readonly prefersSms: boolean;
}

export interface PropertyData {
  readonly propertyType: PropertyType;
  readonly addressLine1: string;
  readonly addressLine2: string;
  readonly city: string;
  readonly state: string;
  readonly postalCode: string;
  readonly accessInstructions: string;
}

export interface ServiceData {
  readonly categoryId: string;
  readonly serviceId: string;
  readonly notSure: boolean;
  readonly description: string;
  readonly urgency: Urgency;
  readonly hasActiveDamage: boolean;
}

export interface AvailabilityData {
  readonly dateMode: DateMode;
  /** Local `YYYY-MM-DD`, only meaningful when `dateMode` is `date`. */
  readonly preferredDate: string;
  readonly timeWindow: TimeWindow;
  readonly schedulingNotes: string;
}

export interface WizardData {
  readonly contact: ContactData;
  readonly property: PropertyData;
  readonly service: ServiceData;
  readonly availability: AvailabilityData;
}

/** JSON of the multipart `request` part. */
export interface ServiceRequestPayload {
  readonly contact: ContactData;
  readonly property: PropertyData;
  readonly service: Omit<ServiceData, 'serviceId'> & { readonly serviceId?: string };
  readonly availability: Omit<AvailabilityData, 'preferredDate'> & {
    readonly preferredDate?: string;
  };
  readonly consent: boolean;
  readonly website: string;
}

/** `201` body. */
export interface ServiceRequestCreated {
  readonly requestNumber: string;
}

/** Field errors keyed by the spec field paths (`contact.email`, `attachments`, `consent`). */
export type StepErrors = Readonly<Record<string, string>>;

/** A property of the signed-in customer offered by the portal wizard (BR-28). */
export interface PortalWizardProperty {
  readonly id: string;
  readonly name: string;
  readonly addressLine1: string;
  readonly city: string;
  readonly stateRegion: string;
  readonly postalCode: string;
}

/** Portal body of the multipart request part: no contact; the property is chosen or new. */
export interface PortalServiceRequestPayload {
  readonly property: { readonly propertyId: string } | { readonly newProperty: PropertyData };
  readonly service: ServiceRequestPayload['service'];
  readonly availability: ServiceRequestPayload['availability'];
  readonly consent: boolean;
  readonly website: string;
}

export interface PortalRequestResult {
  readonly requestId: string;
  readonly requestNumber: string;
}

/**
 * Portal mode of the wizard (customer-portal-dashboard BR-28): no Contact step, the Property step
 * offers the customer's properties, and the page supplies the submission.
 */
export interface PortalWizardConfig {
  readonly properties: readonly PortalWizardProperty[];
  readonly submit: (
    payload: PortalServiceRequestPayload,
    files: readonly File[],
  ) => Observable<PortalRequestResult>;
}

/** Value of the property choice that means "Add a new property". */
export const NEW_PROPERTY_CHOICE = 'new';
