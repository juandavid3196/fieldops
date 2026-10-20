import { Component } from '@angular/core';

import { LegalDocumentView } from '../../components/legal-document-view/legal-document-view';
import { PRIVACY_POLICY } from '../../data/privacy-policy';

@Component({
  selector: 'app-privacy-policy',
  imports: [LegalDocumentView],
  template: '<app-legal-document-view [content]="content" />',
})
export class PrivacyPolicy {
  protected readonly content = PRIVACY_POLICY;
}
