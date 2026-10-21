import {
  CatalogType,
  ITEM_FIELD_KEYS,
  ItemFieldErrors,
  ItemFieldKey,
} from '../../models/catalog.model';
import { normalizeName, parseMoneyCents } from '../../utils/catalog-format';

import { localized, t } from '../../../../core/i18n/translate';
export const TYPE_MESSAGE = (): string => t('productsServices.itemDrawerValidators.typeMessage');
export const NAME_REQUIRED_MESSAGE = (): string =>
  t('productsServices.itemDrawerValidators.nameRequiredMessage');
export const NAME_LENGTH_MESSAGE = (): string =>
  t('productsServices.itemDrawerValidators.nameLengthMessage');
export const DESCRIPTION_MESSAGE = (): string =>
  t('productsServices.itemDrawerValidators.descriptionMessage');
export const COST_MESSAGE = (): string => t('productsServices.itemDrawerValidators.costMessage');
export const PRICE_MESSAGE = (): string => t('productsServices.itemDrawerValidators.priceMessage');
export const DUPLICATE_NAME_MESSAGE = (): string =>
  t('productsServices.itemDrawerValidators.duplicateNameMessage');

export const FIELD_LABELS: Readonly<Record<ItemFieldKey, string>> = localized(() => ({
  type: t('common.type'),
  name: t('common.name'),
  description: t('common.description'),
  category: t('productsServices.itemDrawerValidators.category'),
  unitCost: t('productsServices.itemDrawerValidators.unitCost'),
  unitPrice: t('productsServices.itemDrawerValidators.unitPrice'),
}));

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
      return value.type === null ? TYPE_MESSAGE() : null;
    case 'name': {
      const name = normalizeName(value.name);
      if (name.length === 0) {
        return NAME_REQUIRED_MESSAGE();
      }
      return name.length > 160 ? NAME_LENGTH_MESSAGE() : null;
    }
    case 'description':
      return value.description.trim().length > 1000 ? DESCRIPTION_MESSAGE() : null;
    case 'category':
      return null;
    case 'unitCost':
      return parseMoneyCents(value.unitCost) === null ? COST_MESSAGE() : null;
    case 'unitPrice':
      return parseMoneyCents(value.unitPrice) === null ? PRICE_MESSAGE() : null;
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
