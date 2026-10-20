import { LegalDocument } from '../models/legal.model';
import { LEGAL_COMPANY_NAME, LEGAL_JURISDICTION, LEGAL_SUPPORT_EMAIL } from './legal-placeholders';

export const TERMS_OF_SERVICE: LegalDocument = {
  title: 'Terms of Service',
  lead: 'The rules for accessing and using FieldOps.',
  highlight: 'By using FieldOps, you agree to these Terms.',
  contact: {
    title: 'Questions about these Terms?',
    text: 'If you have questions about these Terms, please contact our support team.',
    subject: 'FieldOps terms question',
  },
  sections: [
    {
      id: 'agreement',
      title: 'Agreement',
      blocks: [
        {
          kind: 'paragraph',
          text: 'These Terms govern your access to and use of FieldOps. If you use FieldOps on behalf of an organization, you confirm that you have authority to accept these Terms for that organization.',
        },
      ],
    },
    {
      id: 'accounts',
      title: 'Accounts',
      blocks: [
        { kind: 'paragraph', text: 'You must:' },
        {
          kind: 'list',
          items: [
            'Provide accurate and current account information.',
            'Keep your credentials secure.',
            'Use only accounts you are authorized to access.',
            'Promptly report unauthorized access or security incidents.',
            'Accept responsibility for activity performed through your account.',
          ],
        },
      ],
    },
    {
      id: 'organization-responsibilities',
      title: 'Organization responsibilities',
      blocks: [
        { kind: 'paragraph', text: 'Organizations are responsible for:' },
        {
          kind: 'list',
          items: [
            'Managing users, roles, permissions, and branch access.',
            'Ensuring that users access only appropriate information.',
            'Collecting and using customer information lawfully.',
            'Maintaining accurate customer, job, billing, and operational records.',
            'Obtaining any permissions required for uploaded files, photos, or messages.',
          ],
        },
      ],
    },
    {
      id: 'acceptable-use',
      title: 'Acceptable use',
      blocks: [
        { kind: 'paragraph', text: 'You must not:' },
        {
          kind: 'list',
          items: [
            'Violate applicable laws or regulations.',
            'Access another organization’s information without authorization.',
            'Upload malicious code or attempt to disrupt the service.',
            'Use FieldOps for fraudulent, abusive, or harmful activity.',
            'Circumvent security or access controls.',
            'Reverse engineer or attempt to extract FieldOps source code.',
            'Use the service in a way that interferes with other users.',
          ],
        },
      ],
    },
    {
      id: 'your-data',
      title: 'Your data',
      blocks: [
        {
          kind: 'paragraph',
          text: 'You retain ownership of the information you or your organization submit to FieldOps.',
        },
        {
          kind: 'paragraph',
          text: `You grant ${LEGAL_COMPANY_NAME} permission to process that information only as needed to provide, secure, support, and improve the service or comply with legal obligations.`,
        },
        {
          kind: 'paragraph',
          text: 'You are responsible for ensuring that you have the right to submit and process this information.',
        },
      ],
    },
    {
      id: 'fieldops-ownership',
      title: 'FieldOps ownership',
      blocks: [
        {
          kind: 'paragraph',
          text: `FieldOps, including its software, interface, documentation, branding, and original content, belongs to ${LEGAL_COMPANY_NAME} and its licensors.`,
        },
        {
          kind: 'paragraph',
          text: 'These Terms do not transfer ownership of FieldOps or grant permission to copy, resell, or create unauthorized derivative products.',
        },
      ],
    },
    {
      id: 'service-availability',
      title: 'Service availability',
      blocks: [
        {
          kind: 'paragraph',
          text: 'We work to keep FieldOps reliable and secure, but uninterrupted availability is not guaranteed. Maintenance, security events, provider failures, or technical issues may temporarily affect access.',
        },
        {
          kind: 'paragraph',
          text: 'Features may be improved, replaced, or discontinued when reasonably necessary.',
        },
      ],
    },
    {
      id: 'third-party-services',
      title: 'Third-party services',
      blocks: [
        {
          kind: 'paragraph',
          text: 'FieldOps may connect with third-party providers or services. Their products and privacy practices are governed by their own terms.',
        },
        {
          kind: 'paragraph',
          text: 'FieldOps is not responsible for third-party services outside its control.',
        },
      ],
    },
    {
      id: 'suspension-and-termination',
      title: 'Suspension and termination',
      blocks: [
        { kind: 'paragraph', text: 'Access may be suspended or terminated when:' },
        {
          kind: 'list',
          items: [
            'These Terms are materially violated.',
            'The service is used unlawfully or creates a security risk.',
            'Required payments remain overdue, where applicable.',
            'An organization or user requests account closure.',
          ],
        },
        {
          kind: 'paragraph',
          text: 'Where reasonable, notice and an opportunity to correct the issue will be provided.',
        },
      ],
    },
    {
      id: 'disclaimers',
      title: 'Disclaimers',
      blocks: [
        {
          kind: 'paragraph',
          text: 'FieldOps is provided on an “as available” basis. To the extent permitted by law, we disclaim warranties that are not expressly stated in these Terms.',
        },
        {
          kind: 'paragraph',
          text: 'Organizations remain responsible for their operational, employment, financial, tax, and customer-service decisions.',
        },
      ],
    },
    {
      id: 'limitation-of-liability',
      title: 'Limitation of liability',
      blocks: [
        {
          kind: 'paragraph',
          text: `To the extent permitted by law, ${LEGAL_COMPANY_NAME} will not be liable for indirect, incidental, special, or consequential losses resulting from the use or inability to use FieldOps.`,
        },
        {
          kind: 'paragraph',
          text: 'Nothing in these Terms excludes liability that cannot legally be limited.',
        },
      ],
    },
    {
      id: 'governing-law',
      title: 'Governing law',
      blocks: [
        {
          kind: 'paragraph',
          text: `These Terms are governed by the laws of ${LEGAL_JURISDICTION}, without regard to conflict-of-law rules.`,
        },
      ],
    },
    {
      id: 'changes',
      title: 'Changes to these Terms',
      blocks: [
        {
          kind: 'paragraph',
          text: 'We may update these Terms when the service or applicable requirements change. Continued use after an update means you accept the revised Terms.',
        },
      ],
    },
    {
      id: 'contact',
      title: 'Contact',
      blocks: [
        {
          kind: 'paragraph',
          text: `Questions about these Terms may be sent to ${LEGAL_SUPPORT_EMAIL} or through Help & support.`,
        },
      ],
    },
  ],
};
