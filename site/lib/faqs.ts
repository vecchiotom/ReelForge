export interface Faq {
  question: string;
  answer: string;
}

// Rendered on the homepage AND emitted as FAQPage structured data from the same array, so the
// visible answer and the answer Google indexes can never drift apart — Google treats FAQ markup
// that isn't visible on the page as a violation, so these must stay a single source.
export const HOME_FAQS: Faq[] = [
  {
    question: 'What does ReelForge actually make?',
    answer:
      'Finished promotional videos for software products: launch videos, feature demos, social ads, onboarding clips and sales outreach. You get an exported video file, ready to publish.',
  },
  {
    question: 'Do I need to give it footage, or does it create the video from scratch?',
    answer:
      'Either. ReelForge can build a video from your product alone, or take raw recordings you already have and edit them into a finished cut. Most teams do both: real footage for the demo, generated scenes for the rest.',
  },
  {
    question: 'Do I need to know how to edit video?',
    answer:
      'No. There is no timeline to learn and no editing seat to buy. You describe what you want, the agents do the scripting, cutting, titling and grading, and you review the result.',
  },
  {
    question: 'How is this different from a template-based video maker?',
    answer:
      'Template tools drop your logo into somebody else’s animation. ReelForge builds the video around your actual product, so what viewers see matches what they get when they sign up.',
  },
  {
    question: 'Who reviews the quality?',
    answer:
      'A dedicated reviewer agent scores every draft against the brief before you see it. Drafts that fall short are sent back through production automatically, so weak cuts never reach your inbox.',
  },
  {
    question: 'Where does my data live, and which AI models are used?',
    answer:
      'You choose. ReelForge connects to your own AI provider account and can run entirely inside your own infrastructure, which means your product, footage and internal material stay under your control.',
  },
  {
    question: 'How long does one video take?',
    answer:
      'Days rather than the weeks an agency cycle takes. Once your first video is set up, re-running it for the next release is a single click.',
  },
];
