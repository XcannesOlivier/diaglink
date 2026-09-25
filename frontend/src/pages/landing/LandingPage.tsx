import { PublicHeader } from '../../components/marketing/PublicHeader';
import { HeroSection } from '../../components/marketing/HeroSection';
import { ProblemSection, DiagnosticDemo, HowItWorksSection } from '../../components/marketing/StorySections';
import { FeaturesSection, FieldSection, TrustSection } from '../../components/marketing/ValueSections';
import { PricingSection, FinalCta, PublicFooter } from '../../components/marketing/ClosingSections';
import styles from './LandingPage.module.css';

export function LandingPage({ isAuthenticated }: { isAuthenticated: boolean }) {
  const loginTarget = isAuthenticated ? '/app' : '/login';
  return (
    <div className={styles.page}>
      <PublicHeader loginTarget={loginTarget} isAuthenticated={isAuthenticated} />
      <main>
        <HeroSection loginTarget={loginTarget} />
        <ProblemSection />
        <DiagnosticDemo />
        <HowItWorksSection />
        <FeaturesSection />
        <FieldSection />
        <TrustSection />
        <PricingSection />
        <FinalCta />
      </main>
      <PublicFooter loginTarget={loginTarget} />
    </div>
  );
}
