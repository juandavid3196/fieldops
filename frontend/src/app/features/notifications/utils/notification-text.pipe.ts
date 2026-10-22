import { Pipe, PipeTransform } from '@angular/core';

import { activeLanguageCode } from '../../../core/i18n/translate';
import { notificationText } from './notification-text';

/** `{{ item.title | notificationText: item.eventCode : 'title' }}` (BR-14). */
@Pipe({ name: 'notificationText', pure: false })
export class NotificationTextPipe implements PipeTransform {
  transform(stored: string, eventCode: string, part: 'title' | 'body'): string {
    activeLanguageCode();
    return notificationText(stored, eventCode, part);
  }
}
