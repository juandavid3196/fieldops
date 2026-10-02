import {
  CatalogType,
  ITEM_FIELD_KEYS,
  ItemFieldErrors,
  ItemFieldKey,
} from '../../models/catalog.model';
import { normalizeName, parseMoneyCents } from '../../utils/catalog-format';

export const TYPE_MESSAGE = 'Select a type.';
export const NAME_REQUIRED_MESSAGE = 'Enter a name.';
export const NAME_LENGTH_MESSAGE = 'Name must be 160 characters or fewer.';
export const DESCRIPTION_MESSAGE = 'Description must be 1,000 characters or fewer.';
export const COST_MESSAGE = 'Enter a unit cost of 0 or more.';
export const PRICE_MESSAGE = 'Enter a unit price of 0 or more.';
export const DUPLICATE_NAME_MESSAGE = 'An item with this name already exists for this type.';

export const FIELD_LABELS: Readonly<Record<ItemFieldKey, string>> = {
  type: 'Type',
  name: 'Name',
  description: 'Description',
  category: 'Category',
  unitCost: 'Unit cost',
  unitPrice: 'Unit price',
};

export interface ItemFormValue {
  readonly type: CatalogType | null;
  readonly name: string;
  readonly description: string;
  readonly unitCost: string;
  readonly unitPrice: string;
}

/** BR-07 message for one field, or `null` when valid. */
export function validateItemField(field: ItemFieldKey, value: ItemFormValue): string | null {
  switch (field) {
    case 'type':
      return value.type === null ? TYPE_MESSAGE : null;
    case 'name': {
      const name = normalizeName(value.name);
      if (name.length === 0) {
        return NAME_REQUIRED_MESSAGE;
      }
      return name.length > 160 ? NAME_LENGTH_MESSAGE : null;
    }
    case 'description':
      return value.description.trim().length > 1000 ? DESCRIPTION_MESSAGE : null;
    case 'category':
      return null;
    case 'unitCost':
      return parseMoneyCents(value.unitCost) === null ? COST_MESSAGE : null;
    case 'unitPrice':
      return parseMoneyCents(value.unitPrice) === null ? PRICE_MESSAGE : null;
  }
}

export function validateItem(value: ItemFormValue): ItemFieldErrors {
  const errors: ItemFieldErrors = {};
  for (const field of ITEM_FIELD_KEYS) {
    const message = validateItemField(field, value);
    if (message !== null) {
      errors[field] = message;
    }
  }
  return errors;
}

/** Maps ProblemDetails `errors` keys (any casing of the first letter) to drawer fields. */
export function mapServerFieldErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): ItemFieldErrors {
  const mapped: ItemFieldErrors = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const field = ITEM_FIELD_KEYS.find(
      (candidate) =>
        candidate.toLowerCase() === key.toLowerCase() ||
        (candidate === 'category' && key.toLowerCase() === 'categoryid'),
    );
    if (field !== undefined && messages[0] !== undefined) {
      mapped[field] = messages[0];
    }
  }
  return mapped;
}
