namespace NexCrm.Domain.Entities;

/// <summary>Three roles are enough for a small team. Only Admin and Manager may delete records.</summary>
public enum UserRole { Admin, Manager, SalesRep }

public enum LeadSource { Website, Referral, LinkedIn, ColdCall, Event, Partner, Ads }

public enum LeadStatus { New, Contacted, Qualified, Unqualified, Converted }

public enum DealStage { Prospecting, Qualification, Proposal, Negotiation, Won, Lost }

public enum TaskType { Call, Email, Meeting, FollowUp }

public enum TaskPriority { Low, Normal, High }

public enum ContactStage { Subscriber, Lead, Opportunity, Customer, Churned }
