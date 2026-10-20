import { LegalDocument } from '../models/legal.model';
import { LEGAL_SUPPORT_EMAIL } from './legal-placeholders';

export const PRIVACY_POLICY: LegalDocument = {
  title: 'Privacy Policy',
  lead: 'How FieldOps collects, uses, and protects information.',
  highlight: 'FieldOps does not sell personal information.',
  contact: {
    title: 'Privacy questions?',
    text: 'If you have questions about this Privacy Policy or our data practices, please contact our support team.',
    subject: 'FieldOps privacy request',
  },
  sections: [
    {
      id: 'overview',
      title: 'Overview',
      blocks: [
        {
          kind: 'paragraph',
          text: 'FieldOps is a field service management platform that helps service organizations manage customers, requests, quotes, jobs, visits, invoices, payments, and team communication.',
        },
        {
          kind: 'paragraph',
          text: 'When an organization uses FieldOps, that organization controls the customer and operational information entered by its users. FieldOps processes that information to provide the service.',
        },
      ],
    },
    {
      id: 'information-we-collect',
      title: 'Information we collect',
      blocks: [
        {
          kind: 'list',
          items: [
            {
              term: 'Account information',
              text: 'Your name, email address, phone number, profile photo, role, branch access, account preferences, and authentication information.',
            },
            {
              term: 'Organization and operational data',
              text: 'Organization settings, branches, team members, permissions, schedules, availability, skills, and operational activity.',
            },
            {
              term: 'Customer and job data',
              text: 'Customer records, properties, service requests, quotes, work orders, visits, checklists, notes, messages, attachments, materials, invoices, payments, and related information.',
            },
            {
              term: 'Usage and security data',
              text: 'IP address, browser and device information, timestamps, audit events, error logs, and information about how FieldOps is used.',
            },
            {
              term: 'Support communications',
              text: 'Information you provide when contacting our support team.',
            },
          ],
        },
      ],
    },
    {
      id: 'how-we-use-information',
      title: 'How we use information',
      blocks: [
        { kind: 'paragraph', text: 'We use information to:' },
        {
          kind: 'list',
          items: [
            'Provide, operate, and improve FieldOps.',
            'Authenticate users and protect accounts.',
            'Process requests and transactions.',
            'Enable communication between authorized users.',
            'Provide customer support.',
            'Monitor performance and prevent misuse.',
            'Maintain audit and security records.',
            'Comply with legal obligations.',
          ],
        },
      ],
    },
    {
      id: 'how-information-is-shared',
      title: 'How information is shared',
      blocks: [
        { kind: 'paragraph', text: 'Information may be shared:' },
        {
          kind: 'list',
          items: [
            'With authorized members of your organization according to their roles and branch access.',
            'With service providers that support hosting, email delivery, storage, security, and other platform operations.',
            'With integrations enabled by your organization.',
            'When required by law or necessary to protect users, FieldOps, or the public.',
            'As part of a merger, acquisition, financing, or transfer of the service.',
          ],
        },
        { kind: 'paragraph', text: 'We do not sell personal information.' },
      ],
    },
    {
      id: 'data-retention',
      title: 'Data retention',
      blocks: [
        {
          kind: 'paragraph',
          text: 'We retain information while it is needed to provide FieldOps, maintain security and audit records, resolve disputes, or comply with legal obligations.',
        },
        {
          kind: 'paragraph',
          text: 'Organizations may retain or delete customer and operational records according to their own policies. Some security, billing, or audit information may be retained when legally required.',
        },
      ],
    },
    {
      id: 'security',
      title: 'Security',
      blocks: [
        {
          kind: 'paragraph',
          text: 'FieldOps uses reasonable technical and organizational safeguards to protect information. These measures include access controls, tenant isolation, secure authentication, audit logging, and encrypted communication.',
        },
        {
          kind: 'paragraph',
          text: 'No system can guarantee absolute security. Users must protect their credentials and promptly report suspected unauthorized access.',
        },
      ],
    },
    {
      id: 'your-rights',
      title: 'Your rights',
      blocks: [
        { kind: 'paragraph', text: 'Depending on your location, you may have the right to:' },
        {
          kind: 'list',
          items: [
            'Access your personal information.',
            'Correct inaccurate information.',
            'Request deletion.',
            'Restrict or object to certain processing.',
            'Request a portable copy of your information.',
            'Withdraw consent where processing depends on consent.',
          ],
        },
        {
          kind: 'paragraph',
          text: 'Requests involving customer or job information may need to be handled by the organization that collected it.',
        },
      ],
    },
    {
      id: 'cookies',
      title: 'Cookies',
      blocks: [
        {
          kind: 'paragraph',
          text: 'FieldOps uses essential cookies or similar technologies for authentication, session security, and preferences. These technologies are necessary for the application to operate correctly.',
        },
      ],
    },
    {
      id: 'childrens-privacy',
      title: 'Children’s privacy',
      blocks: [
        {
          kind: 'paragraph',
          text: 'FieldOps is intended for business use and is not directed to children under 18.',
        },
      ],
    },
    {
      id: 'changes',
      title: 'Changes to this policy',
      blocks: [
        {
          kind: 'paragraph',
          text: 'We may update this Privacy Policy when FieldOps or applicable requirements change. The updated date will appear at the top of this page.',
        },
      ],
    },
    {
      id: 'contact',
      title: 'Contact',
      blocks: [
        {
          kind: 'paragraph',
          text: `For privacy questions or requests, contact us at ${LEGAL_SUPPORT_EMAIL} or use Help & support.`,
        },
      ],
    },
  ],
};
