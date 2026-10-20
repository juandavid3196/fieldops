import { Component } from '@angular/core';

import { LegalDocumentView } from '../../components/legal-document-view/legal-document-view';
import { TERMS_OF_SERVICE } from '../../data/terms-of-service';

@Component({
  selector: 'app-terms-of-service',
  imports: [LegalDocumentView],
  template: '<app-legal-document-view [content]="content" />',
})
export class TermsOfService {
  protected readonly content = TERMS_OF_SERVICE;
}
